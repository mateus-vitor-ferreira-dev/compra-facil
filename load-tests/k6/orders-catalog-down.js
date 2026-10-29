// Teste de caos da #81: pedidos seguem com o catalog fora.
//
// Cria pedidos a uma taxa constante enquanto o `task chaos:catalog` derruba o catalog no meio da
// rodada e o sobe de novo. Com o fallback, o orders precifica pela cópia local do catálogo: nenhum
// pedido pode falhar (202 é o esperado; 409 é recusa de negócio, não falha). O que muda durante a
// queda aparece no Grafana: compra_facil_orders_catalog_fallback_total e o circuito do Polly.
//
// Pré-requisitos: `task up` e `task seed` (a cópia local nasce dos eventos do seed).
// Variáveis: CATALOG_URL, ORDERS_URL e IDENTITY_URL (na rede do compose), RATE (pedidos/s, 20), DURATION (60s).
import http from 'k6/http';
import { check, fail } from 'k6';
import { Counter } from 'k6/metrics';
import { newCustomers } from './identity.js';

const CATALOG_URL = __ENV.CATALOG_URL || 'http://catalog:8001';
const ORDERS_URL = __ENV.ORDERS_URL || 'http://orders:8002';
const IDENTITY_URL = __ENV.IDENTITY_URL || 'http://identity:8006';
const RATE = parseInt(__ENV.RATE || '20', 10);
const DURATION = __ENV.DURATION || '60s';

const unavailable = new Counter('orders_unavailable');

// 202 (aceito) e 409 (recusa do catálogo/cópia) são respostas corretas; 5xx não
http.setResponseCallback(http.expectedStatuses(202, 409));

export const options = {
  scenarios: {
    orders: {
      executor: 'constant-arrival-rate',
      rate: RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
  },
  thresholds: {
    http_req_failed: ['rate<0.01'],
    orders_unavailable: ['count==0'],
    // inclui as tentativas no catalog antes do circuito abrir
    http_req_duration: ['p(95)<3500'],
  },
  summaryTrendStats: ['avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// setup roda com o catalog no ar: pega os produtos que vão ser comprados
export function setup() {
  const res = http.get(`${CATALOG_URL}/v1/products?limit=100`, { responseCallback: http.expectedStatuses(200) });
  if (res.status !== 200) {
    fail(`catálogo respondeu ${res.status}; rode task up e task seed`);
  }
  const ids = res.json('data').map((p) => p.id);
  if (ids.length === 0) {
    fail('catálogo vazio; rode task seed');
  }
  return { ids, tokens: newCustomers(IDENTITY_URL, 'caos', 10) };
}

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const r = (Math.random() * 16) | 0;
    return (c === 'x' ? r : (r & 0x3) | 0x8).toString(16);
  });
}

export default function (data) {
  const product = data.ids[Math.floor(Math.random() * data.ids.length)];
  const token = data.tokens[Math.floor(Math.random() * data.tokens.length)];
  const res = http.post(`${ORDERS_URL}/v1/orders`, JSON.stringify({
    items: [{ product_id: product, quantity: 1 }],
    payment: { method: 'pix' }, // a carga mede o aceite; o PIX fica pendente até expirar
  }), { headers: { 'Content-Type': 'application/json', 'Idempotency-Key': uuid(), Authorization: `Bearer ${token}` } });

  if (res.status === 503) {
    unavailable.add(1);
  }
  check(res, { 'pedido aceito ou recusado pelo negócio': (r) => r.status === 202 || r.status === 409 });
}
