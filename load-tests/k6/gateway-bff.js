// Rate limit por comprador com o BFF do front (#176). O BFF chama as APIs pelo servidor; com o segredo
// dele (X-Bff-Key), o gateway limita pelo IP do comprador (X-Client-IP), e não pelo IP do servidor.
//   1. dois compradores pelo BFF não dividem o limite: um esgota o dele, o outro continua entrando;
//   2. X-Client-IP forjado sem o segredo não escapa: o limite segue pelo IP de origem;
//   3. com o segredo errado, também não.
// Usa o login (5 req/s, rajada de 20), o limite mais apertado. Rodar: task test:gateway-bff (precisa do
// gateway com BFF_GATEWAY_KEY).
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = { vus: 1, iterations: 1, thresholds: { checks: ['rate==1'] } };

const GATEWAY = __ENV.GATEWAY_URL || 'http://localhost';
const KEY = __ENV.BFF_GATEWAY_KEY;
const LOGIN = `${GATEWAY}/identity/v1/sessions`;
const body = JSON.stringify({ email: 'ninguem@exemplo.com', password: 'senha-errada-123' });

/** Rajada de logins (senha errada: 401 enquanto houver limite, 429 quando acaba). */
function burst(headersOf, n = 40) {
  return http.batch(
    Array.from({ length: n }, (_, i) => ({
      method: 'POST',
      url: LOGIN,
      body,
      params: { headers: { 'Content-Type': 'application/json', ...headersOf(i) }, tags: { name: 'login' } },
    })),
  );
}

const limited = (responses) => responses.filter((r) => r.status === 429).length;

export default function () {
  if (!KEY) throw new Error('defina BFF_GATEWAY_KEY (o mesmo do gateway)');

  // 1. o comprador A esgota o limite dele; o B, no mesmo instante, ainda entra
  const a = burst(() => ({ 'X-Bff-Key': KEY, 'X-Client-IP': '203.0.113.10' }));
  const b = http.post(LOGIN, body, {
    headers: { 'Content-Type': 'application/json', 'X-Bff-Key': KEY, 'X-Client-IP': '203.0.113.20' },
  });
  check(a, { 'comprador A chega ao limite': (r) => limited(r) > 0 });
  check(b, { 'comprador B não divide o limite do A': (r) => r.status === 401 });

  // 2. sem o segredo, um X-Client-IP diferente em cada requisição não abre limites novos
  sleep(5); // o limite pelo IP de origem volta a encher
  const forged = burst((i) => ({ 'X-Client-IP': `198.51.100.${i}` }));
  check(forged, { 'X-Client-IP forjado sem o segredo é limitado pela origem': (r) => limited(r) > 0 });

  // 3. com o segredo errado, idem
  sleep(5);
  const wrongKey = burst((i) => ({ 'X-Bff-Key': `${KEY}-errada`, 'X-Client-IP': `198.51.100.${i}` }));
  check(wrongKey, { 'segredo errado é limitado pela origem': (r) => limited(r) > 0 });

  // a vitrine também passa pela rota do BFF
  const catalog = http.get(`${GATEWAY}/catalog/v1/categories`, {
    headers: { 'X-Bff-Key': KEY, 'X-Client-IP': '203.0.113.30' },
  });
  check(catalog, { 'vitrine pelo BFF responde': (r) => r.status === 200 });
}
