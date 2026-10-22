// Hit ratio do cache do catálogo (#67) sob o tráfego de leitura de uma campanha.
//
// A maior parte das visitas cai nos produtos em promoção (o "pico" que o cache absorve); uma
// cauda menor vai a produtos quaisquer, listagens por categoria, categorias e campanhas.
// O hit ratio sai do header X-Cache (HIT | MISS | BYPASS) que o catalog põe nas rotas públicas.
//
// Pré-requisitos: `task up` e `task seed`. Rodar: `task load:catalog-cache`
// Variáveis: BASE_URL (http://localhost:8001; pelo `task load:catalog-cache`, http://catalog:8001 na rede do
// compose), RATE (req/s no pico, 200), DURATION (2m).
//
// Só a fase "peak" entra nos thresholds: o aquecimento existe para encher o cache, e os misses
// dele não dizem nada sobre o regime da campanha.
import http from 'k6/http';
import { check, fail } from 'k6';
import { Rate, Counter } from 'k6/metrics';

const BASE_URL = __ENV.BASE_URL || 'http://localhost:8001';
const RATE = parseInt(__ENV.RATE || '200', 10);
const DURATION = __ENV.DURATION || '2m';
const WARMUP = '30s';

const cacheHit = new Rate('cache_hit');
const cacheBypass = new Counter('cache_bypass');

export const options = {
  scenarios: {
    warmup: {
      executor: 'constant-arrival-rate',
      rate: Math.max(1, Math.floor(RATE / 4)),
      timeUnit: '1s',
      duration: WARMUP,
      preAllocatedVUs: 20,
      maxVUs: 100,
    },
    peak: {
      executor: 'constant-arrival-rate',
      startTime: WARMUP,
      rate: RATE,
      timeUnit: '1s',
      duration: DURATION,
      preAllocatedVUs: 50,
      maxVUs: 300,
    },
  },
  thresholds: {
    'cache_hit{scenario:peak}': ['rate>0.90'],
    'http_req_failed{scenario:peak}': ['rate<0.01'],
    'http_req_duration{scenario:peak}': ['p(95)<200'],
    // sem critério: só para o resumo mostrar o hit ratio de cada tipo de leitura
    'cache_hit{scenario:peak,kind:product}': ['rate>=0'],
    'cache_hit{scenario:peak,kind:products}': ['rate>=0'],
    'cache_hit{scenario:peak,kind:categories}': ['rate>=0'],
    'cache_hit{scenario:peak,kind:campaigns}': ['rate>=0'],
  },
  summaryTrendStats: ['avg', 'p(90)', 'p(95)', 'p(99)', 'max'],
};

const SEARCH_TERMS = ['fone', 'notebook', 'smart', 'cafeteira', 'tenis'];

// collect percorre a listagem pública pelo cursor e devolve os ids encontrados.
function collect(query, maxPages) {
  const ids = [];
  let cursor = '';
  for (let i = 0; i < maxPages; i++) {
    const url = `${BASE_URL}/v1/products?limit=100${query}${cursor ? `&cursor=${encodeURIComponent(cursor)}` : ''}`;
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
  const cats = http.get(`${BASE_URL}/v1/categories`, { tags: { name: 'setup' } });
  if (cats.status !== 200) fail(`catalog fora do ar em ${BASE_URL}? /v1/categories respondeu ${cats.status}`);

  const data = {
    categories: cats.json().data.map((c) => c.slug),
    hot: collect('&on_sale=true', 3), // até 300 produtos em campanha
    all: collect('', 20), // cauda: até 2000 produtos quaisquer
  };
  if (data.hot.length === 0 || data.categories.length === 0) {
    fail('catálogo vazio ou sem campanha ativa: rode `task seed` antes');
  }
  console.log(`setup: ${data.categories.length} categorias, ${data.hot.length} produtos em campanha, ${data.all.length} na cauda`);
  return data;
}

const pick = (list) => list[Math.floor(Math.random() * list.length)];

function read(url, kind) {
  const res = http.get(url, { tags: { name: kind } });
  check(res, { 'status 200': (r) => r.status === 200 });
  const status = res.headers['X-Cache'];
  if (status === 'BYPASS') cacheBypass.add(1, { kind });
  // sem header = cache desligado: conta como miss para não mascarar o resultado
  cacheHit.add(status === 'HIT', { kind });
}

export default function (data) {
  const r = Math.random();
  if (r < 0.55) {
    // detalhe: 90% nos produtos da campanha, 10% em qualquer um
    const id = Math.random() < 0.9 ? pick(data.hot) : pick(data.all);
    read(`${BASE_URL}/v1/products/${id}`, 'product');
  } else if (r < 0.8) {
    const onSale = Math.random() < 0.5 ? '&on_sale=true' : '';
    read(`${BASE_URL}/v1/products?category=${pick(data.categories)}${onSale}`, 'products');
  } else if (r < 0.85) {
    read(`${BASE_URL}/v1/products?q=${pick(SEARCH_TERMS)}`, 'products');
  } else if (r < 0.93) {
    read(`${BASE_URL}/v1/categories`, 'categories');
  } else {
    read(`${BASE_URL}/v1/campaigns`, 'campaigns');
  }
}
