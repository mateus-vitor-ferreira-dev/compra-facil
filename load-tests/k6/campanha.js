// Cenário de campanha promocional (#89): 90% leitura do catálogo, 10% compras, com rampa até o pico.
//
// É a carga do critério observável 1 do parecer (escala independente, #90): o pico de uma campanha
// cai quase todo no catálogo, e a pergunta é se escalar só ele segura p95 e erros.
//
// Vai direto nos serviços, na rede do compose, e não pelo gateway: o rate limit do gateway (50 req/s
// por IP) recusaria quase tudo de um gerador de carga único, e o que se mede aqui é o serviço.
// Com várias réplicas do catalog, o DNS do compose devolve um IP por réplica; o k6 resolve em
// rodízio e sem cache, então as conexões se espalham entre elas.
//
// Pré-requisitos: `task up` e `task seed`. Rodar: `task load` (ou `task load -- -e PEAK_RATE=800`).
// As métricas do k6 vão para o Prometheus (remote write) e aparecem no dashboard Campanha.
//
// Variáveis: CATALOG_URL, ORDERS_URL, IDENTITY_URL (os clientes que compram), BASE_RATE (req/s fora do pico, 50), PEAK_RATE (req/s no pico,
// 400), PEAK (duração do pico, 2m).
import http from 'k6/http';
import { check, fail } from 'k6';
import { newCustomers } from './identity.js';

const CATALOG_URL = __ENV.CATALOG_URL || 'http://catalog:8001';
const ORDERS_URL = __ENV.ORDERS_URL || 'http://orders:8002';
const IDENTITY_URL = __ENV.IDENTITY_URL || 'http://identity:8006';
const BASE_RATE = parseInt(__ENV.BASE_RATE || '50', 10);
const PEAK_RATE = parseInt(__ENV.PEAK_RATE || '400', 10);
const PEAK = __ENV.PEAK || '2m';

const BUY_SHARE = 0.1;
const SEARCH_TERMS = ['fone', 'notebook', 'smart', 'cafeteira', 'tenis'];

// aquecimento → rampa → pico → descida; cada cenário recebe a sua parte da taxa total
function stages(share) {
  const r = (rate) => Math.max(1, Math.round(rate * share));
  return [
    { target: r(BASE_RATE), duration: '30s' },
    { target: r(PEAK_RATE), duration: '30s' },
    { target: r(PEAK_RATE), duration: PEAK },
    { target: r(BASE_RATE), duration: '15s' },
  ];
}

function scenario(exec, share, vus) {
  return {
    executor: 'ramping-arrival-rate',
    exec,
    startRate: Math.max(1, Math.round(BASE_RATE * share)),
    timeUnit: '1s',
    stages: stages(share),
    preAllocatedVUs: vus,
    maxVUs: vus * 5,
  };
}

export const options = {
  dns: { ttl: '0', select: 'roundRobin', policy: 'onlyIPv4' },
  scenarios: {
    browse: scenario('browse', 1 - BUY_SHARE, 100),
    buy: scenario('buy', BUY_SHARE, 20),
  },
  thresholds: {
    // critérios da #89, no tráfego todo (a rampa faz parte da campanha)
    http_req_duration: ['p(95)<300'],
    http_req_failed: ['rate<0.01'],
    // sem critério: só para o resumo separar leitura e compra
    'http_req_duration{scenario:browse}': ['p(95)>=0'],
    'http_req_duration{scenario:buy}': ['p(95)>=0'],
  },
  summaryTrendStats: ['avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

// 202 é o pedido aceito; 409 é recusa de negócio (preço mudou, inativo), não falha do sistema
const orderAccepted = http.expectedStatuses(202, 409);

function collect(query, maxPages) {
  const ids = [];
  let cursor = '';
  for (let i = 0; i < maxPages; i++) {
    const url = `${CATALOG_URL}/v1/products?limit=100${query}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
    const res = http.get(url, { tags: { name: 'setup' } });
    if (res.status !== 200) fail(`setup: ${url} respondeu ${res.status}`);
    const body = res.json();
    body.data.forEach((p) => ids.push(p.id));
    if (!body.page.has_more) break;
    cursor = body.page.next_cursor;
  }
  return ids;
}

export function setup() {
  const cats = http.get(`${CATALOG_URL}/v1/categories`, { tags: { name: 'setup' } });
  if (cats.status !== 200) fail(`catalog fora do ar em ${CATALOG_URL}? /v1/categories respondeu ${cats.status}`);
  const data = {
    categories: cats.json().data.map((c) => c.slug),
    hot: collect('&on_sale=true', 3),
    all: collect('', 20),
    customers: newCustomers(IDENTITY_URL, 'campanha', 20),
  };
  if (data.hot.length === 0 || data.categories.length === 0) {
    fail('catálogo vazio ou sem campanha ativa: rode `task seed` antes');
  }
  return data;
}

const pick = (list) => list[Math.floor(Math.random() * list.length)];

function read(url, name) {
  const res = http.get(url, { tags: { name } });
  check(res, { 'leitura 200': (r) => r.status === 200 });
}

// vitrine da campanha: a maior parte nos produtos em promoção
export function browse(data) {
  const r = Math.random();
  if (r < 0.55) {
    read(`${CATALOG_URL}/v1/products/${Math.random() < 0.9 ? pick(data.hot) : pick(data.all)}`, 'product');
  } else if (r < 0.8) {
    const onSale = Math.random() < 0.5 ? '&on_sale=true' : '';
    read(`${CATALOG_URL}/v1/products?category=${pick(data.categories)}${onSale}`, 'products');
  } else if (r < 0.85) {
    read(`${CATALOG_URL}/v1/products?q=${pick(SEARCH_TERMS)}`, 'search');
  } else if (r < 0.93) {
    read(`${CATALOG_URL}/v1/categories`, 'categories');
  } else {
    read(`${CATALOG_URL}/v1/campaigns`, 'campaigns');
  }
}

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const n = (Math.random() * 16) | 0;
    return (c === 'x' ? n : (n & 0x3) | 0x8).toString(16);
  });
}

// compra de um produto da campanha: o orders valida e precifica no catalog (gRPC) e inicia a SAGA
export function buy(data) {
  const res = http.post(`${ORDERS_URL}/v1/orders`, JSON.stringify({
    items: [{ product_id: pick(data.hot), quantity: 1 }],
  }), {
    headers: { 'Content-Type': 'application/json', 'Idempotency-Key': uuid(), Authorization: `Bearer ${pick(data.customers)}` },
    tags: { name: 'order' },
    responseCallback: orderAccepted,
  });
  check(res, { 'pedido aceito': (r) => r.status === 202 || r.status === 409 });
}
