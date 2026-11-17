// Clientes de teste no identity: cadastro + login, para os scripts comprarem com o token de um cliente
// (o orders tira o cliente e o e-mail do token). Usado no setup(): o token vale 15 min, mais que
// qualquer rodada destes scripts.
import http from 'k6/http';
import crypto from 'k6/crypto';
import { fail, sleep } from 'k6';

const json = { 'Content-Type': 'application/json' };
// cada chamada declara o próprio sucesso: os scripts trocam o padrão (o de caos aceita só 202/409)

function suffix() {
  return `${Date.now().toString(36)}${Math.floor(Math.random() * 1e9).toString(36)}`;
}

// login de uma conta que já existe; devolve o access token. A conta com 2FA (o operador) responde 202: o
// segundo passo usa o código da chave TOTP do ambiente local (ADMIN_TOTP_KEY), e quem chamava login() para o
// operador continua recebendo um token que as rotas de admin aceitam
export function login(identityUrl, email, password) {
  const res = http.post(`${identityUrl}/v1/sessions`, JSON.stringify({ email, password }),
    { headers: json, tags: { name: 'login' }, responseCallback: http.expectedStatuses(200, 202) });
  if (res.status === 202) return secondFactor(identityUrl, email, res, __ENV.ADMIN_TOTP_KEY).access;
  if (res.status !== 200) fail(`login de ${email}: ${res.status} ${res.body}`);
  return res.json('access_token');
}

// o código TOTP (RFC 6238: HMAC-SHA1, 6 dígitos, 30 s) da chave Base32, como o app autenticador mostraria
export function totp(base32Key, at = Date.now()) {
  const alphabet = 'ABCDEFGHIJKLMNOPQRSTUVWXYZ234567';
  const bytes = [];
  let buffer = 0;
  let bits = 0;
  for (const c of base32Key.replace(/[\s=]/g, '').toUpperCase()) {
    buffer = (buffer << 5) | alphabet.indexOf(c);
    bits += 5;
    if (bits >= 8) {
      bytes.push((buffer >> (bits - 8)) & 0xff);
      bits -= 8;
    }
  }
  const counter = new Uint8Array(8);
  let step = Math.floor(at / 1000 / 30);
  for (let i = 7; i >= 0; i--) {
    counter[i] = step & 0xff;
    step = Math.floor(step / 256);
  }
  const hex = crypto.hmac('sha1', new Uint8Array(bytes).buffer, counter.buffer, 'hex');
  const offset = parseInt(hex.slice(-1), 16);
  const binary = parseInt(hex.slice(offset * 2, offset * 2 + 8), 16) & 0x7fffffff;
  return String(binary % 1000000).padStart(6, '0');
}

// login do operador, com o segundo fator (a 2FA é obrigatória para o admin): a senha dá o tíquete, o código
// da chave TOTP do ambiente local (ADMIN_TOTP_KEY) abre a sessão. Devolve { access, refresh }. O mesmo código
// não vale duas vezes: se outro login já o usou, espera o próximo
export function adminLogin(identityUrl, email, password, totpKey) {
  const first = http.post(`${identityUrl}/v1/sessions`, JSON.stringify({ email, password }),
    { headers: json, tags: { name: 'login' }, responseCallback: http.expectedStatuses(202) });
  return secondFactor(identityUrl, email, first, totpKey);
}

// o segundo passo a partir do 202 do login: { access, refresh }
function secondFactor(identityUrl, email, first, totpKey) {
  if (first.status !== 202 || first.json('status') !== 'mfa_required' || !totpKey) {
    fail(`login do operador ${email}: ${first.status} ${first.body} (a chave ADMIN_TOTP_KEY está no identity e no k6?)`);
  }
  const ticket = first.json('mfa_ticket');
  for (let attempt = 0; attempt < 2; attempt++) {
    const res = http.post(`${identityUrl}/v1/sessions/mfa`, JSON.stringify({ ticket, code: totp(totpKey) }),
      { headers: json, tags: { name: 'login-mfa' }, responseCallback: http.expectedStatuses(200, 422) });
    if (res.status === 200) return { access: res.json('access_token'), refresh: res.json('refresh_token') };
    if (res.status !== 422) break;
    sleep(31 - ((Date.now() / 1000) % 30)); // o código deste passo já foi usado (outro login): o próximo
  }
  fail(`segundo fator do operador ${email} recusado`);
}

// troca o refresh token do operador por um par novo (o amr do segundo fator continua): { access, refresh }
export function refreshSession(identityUrl, refresh) {
  const res = http.post(`${identityUrl}/v1/sessions/refresh`, JSON.stringify({ refresh_token: refresh }),
    { headers: json, tags: { name: 'refresh' }, responseCallback: http.expectedStatuses(200) });
  if (res.status !== 200) fail(`refresh: ${res.status} ${res.body}`);
  return { access: res.json('access_token'), refresh: res.json('refresh_token') };
}

// cadastra um cliente novo e faz login: { email, token }
// tag: "ana-x+recusar@..." (o fornecedor simulado lê o cenário do que vem depois do +)
export function newCustomer(identityUrl, label, tag) {
  const email = `${label}-${suffix()}${tag ? `+${tag}` : ''}@exemplo.com`;
  const password = `senha-${suffix()}`;
  // o nome aceita só letras, espaços, apóstrofo, hífen e ponto: os dígitos do label ficam fora
  const name = `Cliente ${label.replace(/[^A-Za-z-]+/g, ' ')}`.replace(/\s+/g, ' ').trim();
  const res = http.post(`${identityUrl}/v1/accounts`, JSON.stringify({ email, password, name }),
    { headers: json, tags: { name: 'register' }, responseCallback: http.expectedStatuses(201) });
  if (res.status !== 201) fail(`cadastro de ${email}: ${res.status} ${res.body}`);
  return { email, token: login(identityUrl, email, password) };
}

// n clientes para a carga se espalhar entre vários "Meus pedidos"
export function newCustomers(identityUrl, label, n) {
  return Array.from({ length: n }, (_, i) => newCustomer(identityUrl, `${label}${i}`).token);
}
