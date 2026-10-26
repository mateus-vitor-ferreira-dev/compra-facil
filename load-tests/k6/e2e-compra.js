// Teste ponta a ponta da compra (#83), pelo gateway, com o ambiente inteiro no ar (compose).
//
// Três cenários, cada um com o próprio produto criado pelo admin do catálogo (o teste não depende
// do seed nem do estoque que outros testes consumiram):
//   1. compra confirmada: reserva → cobrança aprovada → pedido confirmed, reserva committed, e-mail;
//   2. estoque insuficiente: estoque zerado pelo admin do inventory → cancelled (out_of_stock);
//   3. pagamento recusado: produto acima do limite do gateway fake (Payments:Fake:MaxApprovedAmountCents)
//      → cancelled (payment_failed), cobrança failed (insufficient_funds) e a reserva devolvida ao estoque.
// Cada cenário compra com um cliente novo, cadastrado e logado no identity: o pedido sai do token, o
// cliente o vê em "Meus pedidos" e outro cliente não o vê.
//
// Rodar: task e2e. No CI: .github/workflows/e2e.yml. O admin entra pelo identity, como o painel faria.
// Variáveis: GATEWAY_URL (http://gateway), MAILPIT_URL (http://mailpit:8025), ADMIN_EMAIL e
// ADMIN_PASSWORD (o operador inicial do compose).
import http from 'k6/http';
import { check, fail, group, sleep } from 'k6';
import { login, newCustomer } from './identity.js';

const GATEWAY = __ENV.GATEWAY_URL || 'http://gateway';
const MAILPIT = __ENV.MAILPIT_URL || 'http://mailpit:8025';
const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@comprafacil.local';
const ADMIN_PASSWORD = __ENV.ADMIN_PASSWORD || 'admin-compra-facil';

const IDENTITY = `${GATEWAY}/identity`;
// acima do limite aprovado pelo gateway fake (R$ 50.000,00 por padrão): cobrança recusada
const ABOVE_FAKE_LIMIT_CENTS = 6000000;
const SAGA_TIMEOUT_S = 60;

export const options = {
  iterations: 1,
  vus: 1,
  // o inventory consome os 10 mil ProductCreated do seed antes dos produtos do teste: no CI passa de 1 min
  setupTimeout: '5m',
  thresholds: { checks: ['rate==1'] }, // qualquer verificação falhando reprova o teste
};

function uuid() {
  return 'xxxxxxxx-xxxx-4xxx-yxxx-xxxxxxxxxxxx'.replace(/[xy]/g, (c) => {
    const n = (Math.random() * 16) | 0;
    return (c === 'x' ? n : (n & 0x3) | 0x8).toString(16);
  });
}

const json = (body) => JSON.stringify(body);
const auth = (token) => ({ 'Content-Type': 'application/json', Authorization: `Bearer ${token}` });

// espera fn() devolver algo verdadeiro (a SAGA e os consumers são assíncronos)
function eventually(what, fn, timeoutS = SAGA_TIMEOUT_S) {
  const deadline = Date.now() + timeoutS * 1000;
  while (Date.now() < deadline) {
    const v = fn();
    if (v) return v;
    sleep(0.5);
  }
  // conta como verificação reprovada antes de abortar: sem isso o threshold de checks não pega
  check(null, { [`a tempo: ${what}`]: () => false });
  fail(`tempo esgotado esperando: ${what}`);
}

function stockOf(productId) {
  const res = http.get(`${GATEWAY}/inventory/v1/stock/${productId}`, { tags: { name: 'stock' } });
  return res.status === 200 ? res.json() : null;
}

function createProduct(token, categoryId, label, priceCents = 1990) {
  const sku = `E2E-${label}-${Math.floor(Math.random() * 1e8)}`.toUpperCase().slice(0, 32);
  const res = http.post(`${GATEWAY}/catalog/v1/admin/products`, json({
    sku, name: `Produto e2e ${label}`, category_id: categoryId, price_cents: priceCents, status: 'active',
  }), { headers: auth(token), tags: { name: 'admin-product' } });
  if (res.status !== 201) fail(`criar produto ${label}: ${res.status} ${res.body}`);
  const id = res.json('id');
  // o inventory cria o saldo ao consumir o ProductCreated
  eventually(`saldo do produto ${label} no inventory`, () => stockOf(id), 240);
  return id;
}

export function setup() {
  // o operador entra pelo identity, como o painel faria
  const token = login(IDENTITY, ADMIN_EMAIL, ADMIN_PASSWORD);
  const cats = http.get(`${GATEWAY}/catalog/v1/categories`);
  if (cats.status !== 200 || cats.json('data').length === 0) fail('sem categorias no catálogo: rode o seed antes');
  const categoryId = cats.json('data')[0].id;

  const products = {
    happy: createProduct(token, categoryId, 'ok'),
    noStock: createProduct(token, categoryId, 'sem-estoque'),
    declined: createProduct(token, categoryId, 'recusado', ABOVE_FAKE_LIMIT_CENTS),
  };

  // zera o estoque do produto do cenário 2
  const level = stockOf(products.noStock);
  if (level.on_hand > 0) {
    const adj = http.post(`${GATEWAY}/inventory/v1/admin/stock/${products.noStock}/adjustments`,
      json({ delta: -level.on_hand, reason: 'inventory_count', note: 'e2e: sem estoque' }),
      { headers: { ...auth(token), 'Idempotency-Key': uuid() } });
    if (adj.status !== 200) fail(`zerar estoque: ${adj.status} ${adj.body}`);
  }
  return products;
}

function placeOrder(productId, customer) {
  const res = http.post(`${GATEWAY}/orders/v1/orders`, json({ items: [{ product_id: productId, quantity: 1 }] }), {
    headers: { ...auth(customer.token), 'Idempotency-Key': uuid() }, tags: { name: 'order' },
  });
  check(res, { 'pedido aceito (202 pending)': (r) => r.status === 202 && r.json('status') === 'pending' });
  if (res.status !== 202) fail(`pedido recusado: ${res.status} ${res.body}`);
  return res.json('id');
}

function finalOrder(orderId, customer) {
  return eventually(`pedido ${orderId} sair de pending`, () => {
    const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
    return res.status === 200 && res.json('status') !== 'pending' ? res.json() : null;
  });
}

function paymentOf(orderId) {
  return eventually(`pagamento do pedido ${orderId}`, () => {
    const res = http.get(`${GATEWAY}/payments/v1/payments?order_id=${orderId}`, { tags: { name: 'payments' } });
    const data = res.status === 200 ? res.json('data') : [];
    return data.length > 0 && data[0].status !== 'processing' ? data[0] : null;
  });
}

function reservationOf(orderId) {
  const res = http.get(`${GATEWAY}/inventory/v1/reservations/${orderId}`, { tags: { name: 'reservation' } });
  return res.status === 200 ? res.json() : null;
}

function emailsTo(address) {
  const res = http.get(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(`to:${address}`)}`, { tags: { name: 'mailpit' } });
  return res.status === 200 ? res.json('messages_count') : 0;
}

export default function (products) {
  group('1. compra confirmada', () => {
    const customer = newCustomer(IDENTITY, 'e2e-ok');
    const orderId = placeOrder(products.happy, customer);
    const order = finalOrder(orderId, customer);
    check(order, { 'pedido confirmed': (o) => o.status === 'confirmed' });
    check(paymentOf(orderId), { 'cobrança approved': (p) => p.status === 'approved' });
    check(eventually('reserva committed', () => {
      const r = reservationOf(orderId);
      return r && r.status === 'committed' ? r : null;
    }), { 'reserva committed': (r) => r.status === 'committed' });
    // o e-mail do aviso é o do token, não veio no corpo do pedido
    check(eventually('e-mail de confirmação no Mailpit', () => emailsTo(customer.email)), { 'e-mail enviado': (n) => n >= 1 });

    const mine = http.get(`${GATEWAY}/orders/v1/me/orders`, { headers: auth(customer.token), tags: { name: 'my-orders' } });
    check(mine, { 'pedido em Meus pedidos': (r) => r.status === 200 && r.json('data').some((o) => o.id === orderId) });
    const stranger = newCustomer(IDENTITY, 'e2e-outro');
    const peek = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(stranger.token), tags: { name: 'order-status' } });
    check(peek, { 'outro cliente não vê o pedido (404)': (r) => r.status === 404 });
  });

  group('2. estoque insuficiente', () => {
    const customer = newCustomer(IDENTITY, 'e2e-sem-estoque');
    const orderId = placeOrder(products.noStock, customer);
    const order = finalOrder(orderId, customer);
    check(order, {
      'pedido cancelled': (o) => o.status === 'cancelled',
      'motivo out_of_stock': (o) => o.cancellation_reason === 'out_of_stock',
    });
    const charges = http.get(`${GATEWAY}/payments/v1/payments?order_id=${orderId}`).json('data');
    check(charges, { 'nenhuma cobrança': (d) => d.length === 0 });
  });

  group('3. pagamento recusado', () => {
    const before = stockOf(products.declined).available;
    const customer = newCustomer(IDENTITY, 'e2e-recusado');
    const orderId = placeOrder(products.declined, customer);
    const order = finalOrder(orderId, customer);
    check(order, {
      'pedido cancelled': (o) => o.status === 'cancelled',
      'motivo payment_failed': (o) => o.cancellation_reason === 'payment_failed',
    });
    check(paymentOf(orderId), {
      'cobrança failed': (p) => p.status === 'failed',
      'motivo insufficient_funds': (p) => p.failure_reason === 'insufficient_funds',
    });
    // compensação: a reserva volta ao estoque
    check(eventually('reserva released', () => {
      const r = reservationOf(orderId);
      return r && r.status === 'released' ? r : null;
    }), { 'reserva released': (r) => r.status === 'released' });
    check(eventually('estoque devolvido', () => {
      const s = stockOf(products.declined);
      return s && s.available === before ? s : null;
    }), { 'estoque disponível de volta': (s) => s.available === before });
  });
}
