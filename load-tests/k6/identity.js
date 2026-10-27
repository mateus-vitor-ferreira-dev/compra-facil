// Clientes de teste no identity: cadastro + login, para os scripts comprarem com o token de um cliente
// (o orders tira o cliente e o e-mail do token). Usado no setup(): o token vale 15 min, mais que
// qualquer rodada destes scripts.
import http from 'k6/http';
import { fail } from 'k6';

const json = { 'Content-Type': 'application/json' };
// cada chamada declara o próprio sucesso: os scripts trocam o padrão (o de caos aceita só 202/409)

function suffix() {
  return `${Date.now().toString(36)}${Math.floor(Math.random() * 1e9).toString(36)}`;
}

// login de uma conta que já existe; devolve o access token
export function login(identityUrl, email, password) {
  const res = http.post(`${identityUrl}/v1/sessions`, JSON.stringify({ email, password }),
    { headers: json, tags: { name: 'login' }, responseCallback: http.expectedStatuses(200) });
  if (res.status !== 200) fail(`login de ${email}: ${res.status} ${res.body}`);
  return res.json('access_token');
}

// cadastra um cliente novo e faz login: { email, token }
export function newCustomer(identityUrl, label) {
  const email = `${label}-${suffix()}@exemplo.com`;
  const password = `senha-${suffix()}`;
  const res = http.post(`${identityUrl}/v1/accounts`, JSON.stringify({ email, password, name: `Cliente ${label}` }),
    { headers: json, tags: { name: 'register' }, responseCallback: http.expectedStatuses(201) });
  if (res.status !== 201) fail(`cadastro de ${email}: ${res.status} ${res.body}`);
  return { email, token: login(identityUrl, email, password) };
}

// n clientes para a carga se espalhar entre vários "Meus pedidos"
export function newCustomers(identityUrl, label, n) {
  return Array.from({ length: n }, (_, i) => newCustomer(identityUrl, `${label}${i}`).token);
}
