// Teste ponta a ponta da compra (#83), pelo gateway, com o ambiente inteiro no ar (compose).
//
// Quatro cenários, cada um com o próprio produto criado pelo admin do catálogo (o teste não depende
// do seed nem do estoque que outros testes consumiram):
//   1. compra confirmada no cartão (titular APRO no payment-sim): reserva → cartão autorizado → pedido
//      confirmed, reserva committed, e-mail; segue o fornecedor simulado até delivered, com o rastreio, e o
//      cartão é capturado quando o fornecedor aceita; depois o cliente desiste (#223) e o valor todo, com o
//      frete, é estornado, com os e-mails da devolução e do estorno;
//   2. estoque insuficiente: estoque zerado pelo admin do inventory → cancelled (out_of_stock);
//   3. pagamento recusado: cartão do titular FUND → cancelled (payment_failed), cobrança failed
//      (insufficient_funds) e a reserva devolvida ao estoque;
//   4. fornecedor recusa: cliente "+recusar" (o fulfillment repassa o cenário ao simulado) → autorizado,
//      cancelled (supplier_failed), a autorização cancelada (voided: nada cobrado), refunded_at no pedido e o
//      e-mail do cancelamento;
//   5. acabou no fornecedor: um produto do feed do simulado esgota lá → o webhook de estoque zera o
//      espelho do inventory em segundos, e o pedido é recusado (out_of_stock) antes de qualquer cobrança;
//   6. PIX: o pedido mostra o QR, o "banco" paga pela página do payment-sim → confirmed, cobrança approved;
//   7. nota fiscal recusada: cliente "+recusar-nota" (a SEFAZ simulada do invoice-sim rejeita) → o pedido
//      pago não vai ao fornecedor, cancelled (invoice_failed) e a autorização do cartão cancelada.
// No cenário 1, a nota fiscal sai antes do fornecedor, aparece no pedido, vai por e-mail e o DANFE baixa.
// Cada cenário compra com um cliente novo, cadastrado e logado no identity: o pedido sai do token, o
// cliente o vê em "Meus pedidos" e outro cliente não o vê.
//
// Rodar: task e2e. No CI: .github/workflows/e2e.yml. O admin entra pelo identity, como o painel faria.
// Variáveis: GATEWAY_URL (http://gateway), MAILPIT_URL (http://mailpit:8025), ADMIN_EMAIL e
// ADMIN_PASSWORD (o operador inicial do compose).
import http from 'k6/http';
import { check, fail, group, sleep } from 'k6';
import { login, newCustomer } from './identity.js';
import { DELIVERY } from './delivery.js';

const GATEWAY = __ENV.GATEWAY_URL || 'http://gateway';
const MAILPIT = __ENV.MAILPIT_URL || 'http://mailpit:8025';
const ADMIN_EMAIL = __ENV.ADMIN_EMAIL || 'admin@comprafacil.local';
const ADMIN_PASSWORD = __ENV.ADMIN_PASSWORD || 'admin-compra-facil';

const IDENTITY = `${GATEWAY}/identity`;
// o fornecedor simulado, pela rede do compose (as rotas /sim não passam pelo gateway)
const SUPPLIER_SIM = __ENV.SUPPLIER_SIM_URL || 'http://supplier-sim:8090';
// uma variante do feed do simulado (Wireless Optical Mouse), importada pelo catalog
const SUPPLIER_VID = 'SIM-V-007';
// o provedor de pagamento simulado (Mercado Pago), pela rede do compose
const PAYMENT_SIM = __ENV.PAYMENT_SIM_URL || 'http://payment-sim:8090';
const PAYMENT_SIM_PUBLIC_KEY = __ENV.PAYMENT_SIM_PUBLIC_KEY || 'TEST-sim-public-key';
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
    declined: createProduct(token, categoryId, 'recusado'),
    pix: createProduct(token, categoryId, 'pix'),
    supplierRejected: createProduct(token, categoryId, 'fornecedor-recusa'),
    invoiceRejected: createProduct(token, categoryId, 'nota-recusada'),
    // o produto que o catalog importou do feed do fornecedor (o fulfillment lê o feed ao subir)
    fromSupplier: eventually(`produto ${SUPPLIER_VID} importado do fornecedor`, () => {
      const res = http.get(`${GATEWAY}/catalog/v1/admin/products?sku=${SUPPLIER_VID}`, { headers: auth(token) });
      const found = res.status === 200 ? res.json('data') : [];
      return found.length > 0 && found[0].status === 'active' ? found[0].id : null;
    }, 120),
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

// o cartão tokenizado no provedor (como o checkout faz); o titular decide o resultado no payment-sim
function card(holder) {
  const res = http.post(`${PAYMENT_SIM}/v1/card_tokens?public_key=${PAYMENT_SIM_PUBLIC_KEY}`, json({
    card_number: '4235647728025682', security_code: '123', expiration_month: 12, expiration_year: 2030,
    cardholder: { name: holder },
  }), { headers: { 'Content-Type': 'application/json' }, tags: { name: 'card-token' } });
  if (res.status !== 201) fail(`token do cartão: ${res.status} ${res.body}`);
  return { method: 'card', card_token: res.json('id'), card_brand: res.json('payment_method_id'), installments: 1 };
}

function placeOrder(productId, customer, payment = card('APRO')) {
  const res = http.post(`${GATEWAY}/orders/v1/orders`, json({ items: [{ product_id: productId, quantity: 1 }], payment, ...DELIVERY }), {
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

// o pedido pago andando pelo fornecedor, até o status dado
function orderReaching(orderId, customer, status) {
  return eventually(`pedido ${orderId} chegar a ${status}`, () => {
    const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
    return res.status === 200 && res.json('status') === status ? res.json() : null;
  });
}

// o pagamento do pedido, esperando chegar a um dos status (sem status: qualquer desfecho)
function paymentOf(orderId, ...statuses) {
  return eventually(`pagamento do pedido ${orderId} ${statuses.join('/')}`, () => {
    const res = http.get(`${GATEWAY}/payments/v1/payments?order_id=${orderId}`, { tags: { name: 'payments' } });
    const data = res.status === 200 ? res.json('data') : [];
    const p = data.length > 0 ? data[0] : null;
    if (!p || p.status === 'processing') return null;
    return statuses.length === 0 || statuses.includes(p.status) ? p : null;
  });
}

function reservationOf(orderId) {
  const res = http.get(`${GATEWAY}/inventory/v1/reservations/${orderId}`, { tags: { name: 'reservation' } });
  return res.status === 200 ? res.json() : null;
}

// e-mails para o endereço com o assunto dado (o cliente recebe o de confirmação da conta e o do pedido)
function emailsTo(address, subject) {
  const query = `to:${address} subject:"${subject}"`;
  const res = http.get(`${MAILPIT}/api/v1/search?query=${encodeURIComponent(query)}`, { tags: { name: 'mailpit' } });
  return res.status === 200 ? res.json('messages_count') : 0;
}

export default function (products) {
  group('1. compra confirmada', () => {
    const customer = newCustomer(IDENTITY, 'e2e-ok');
    const orderId = placeOrder(products.happy, customer);
    const order = finalOrder(orderId, customer);
    check(order, { 'pedido confirmed': (o) => o.status === 'confirmed' });
    check(paymentOf(orderId, 'authorized', 'approved'), { 'cartão autorizado': (p) => p.method === 'card' });
    check(eventually('reserva committed', () => {
      const r = reservationOf(orderId);
      return r && r.status === 'committed' ? r : null;
    }), { 'reserva committed': (r) => r.status === 'committed' });
    // o e-mail do aviso é o do token, não veio no corpo do pedido
    check(eventually('e-mail do pedido no Mailpit', () => emailsTo(customer.email, 'confirmado')), { 'e-mail enviado': (n) => n >= 1 });
    // o cadastro publicou o pedido de confirmação de e-mail (outbox do identity) e o notifications o mandou
    check(eventually('e-mail de confirmação da conta', () => emailsTo(customer.email, 'Confirme o seu e-mail')),
      { 'e-mail de confirmação da conta': (n) => n === 1 });

    // o fornecedor simulado aceita, envia e entrega sozinho (SIM_*_DELAY, uns 17 s no compose)
    check(orderReaching(orderId, customer, 'delivered'), {
      'pedido delivered': (o) => o.status === 'delivered',
      'com o rastreio': (o) => !!(o.tracking && o.tracking.number),
      'aceito pelo fornecedor antes do envio': (o) => !!o.sent_to_supplier_at && o.sent_to_supplier_at <= o.shipped_at,
      'com a nota fiscal autorizada': (o) => !!(o.invoice && /^\d{44}$/.test(o.invoice.access_key)),
      'com o frete no total e o prazo prometido': (o) => !!o.shipping && o.shipping.price.amount_cents > 0
        && o.total.amount_cents === o.items_total.amount_cents + o.shipping.price.amount_cents && !!o.shipping.promised_by,
    });
    // o fornecedor aceitou: a autorização foi capturada
    check(paymentOf(orderId, 'approved'), { 'cartão capturado': (p) => !!p.captured_at });
    // a nota fiscal: o e-mail com o link do pedido e o DANFE para o dono baixar pelo gateway
    check(eventually('e-mail da nota fiscal', () => emailsTo(customer.email, 'Nota fiscal')), { 'e-mail da nota fiscal': (n) => n >= 1 });
    const danfe = http.get(`${GATEWAY}/fulfillment/v1/me/invoices/${orderId}/danfe`, { headers: auth(customer.token), tags: { name: 'danfe' } });
    check(danfe, { 'DANFE em PDF': (r) => r.status === 200 && String(r.headers['Content-Type']).startsWith('application/pdf') });

    const mine = http.get(`${GATEWAY}/orders/v1/me/orders`, { headers: auth(customer.token), tags: { name: 'my-orders' } });
    check(mine, { 'pedido em Meus pedidos': (r) => r.status === 200 && r.json('data').some((o) => o.id === orderId) });
    const stranger = newCustomer(IDENTITY, 'e2e-outro');
    const peek = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(stranger.token), tags: { name: 'order-status' } });
    check(peek, { 'outro cliente não vê o pedido (404)': (r) => r.status === 404 });

    // arrependimento (#223): o entregue volta em 7 dias, e o cartão capturado é estornado com o frete
    const back = http.post(`${GATEWAY}/orders/v1/orders/${orderId}/return`, null, { headers: auth(customer.token), tags: { name: 'order-return' } });
    check(back, { 'desistência aceita (returned)': (r) => r.status === 200 && r.json('status') === 'returned' });
    check(eventually('pedido devolvido estornado', () => {
      const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
      return res.status === 200 && res.json('refunded_at') ? res.json() : null;
    }), { 'devolvido e estornado': (o) => o.status === 'returned' && !!o.return_requested_at });
    check(paymentOf(orderId, 'refunded'), { 'estorno do valor todo': (p) => p.refunded_cents === p.amount.amount_cents });
    check(eventually('e-mail da devolução', () => emailsTo(customer.email, 'Devolução do pedido')), { 'e-mail da devolução': (n) => n >= 1 });
    check(eventually('e-mail do estorno', () => emailsTo(customer.email, 'Estorno do pedido')), { 'e-mail do estorno': (n) => n >= 1 });
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
    const orderId = placeOrder(products.declined, customer, card('FUND'));
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

  group('4. fornecedor recusa', () => {
    const customer = newCustomer(IDENTITY, 'e2e-fornecedor', 'recusar');
    const orderId = placeOrder(products.supplierRejected, customer);
    check(finalOrder(orderId, customer), { 'pedido pago (confirmed)': (o) => o.status === 'confirmed' });
    // compensação: o fornecedor recusou o pedido pago, e o cliente é estornado
    const order = eventually('pedido cancelado e estornado', () => {
      const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
      return res.status === 200 && res.json('refunded_at') ? res.json() : null;
    });
    check(order, {
      'pedido cancelled': (o) => o.status === 'cancelled',
      'motivo supplier_failed': (o) => o.cancellation_reason === 'supplier_failed',
    });
    // o cartão só estava autorizado: a autorização é cancelada, nada chega a ser cobrado
    check(paymentOf(orderId, 'voided'), { 'autorização cancelada': (p) => p.status === 'voided' });
    check(eventually('e-mail do cancelamento', () => emailsTo(customer.email, 'cancelado')), { 'e-mail do cancelamento': (n) => n >= 1 });
  });

  group('5. acabou no fornecedor', () => {
    const product = products.fromSupplier;
    const set = (n) => http.put(`${SUPPLIER_SIM}/sim/products/${SUPPLIER_VID}/stock`, json({ stock: n }), {
      headers: { 'Content-Type': 'application/json' }, tags: { name: 'sim-stock' },
    });
    check(eventually('espelho com estoque', () => {
      const s = stockOf(product);
      return s && s.available > 0 ? s : null;
    }), { 'produto do fornecedor à venda': (s) => s.available > 0 });

    check(set(0), { 'fornecedor esgotou': (r) => r.status === 200 });
    // o webhook de estoque → SupplierStockChanged → o espelho do inventory zera
    check(eventually('espelho zerado', () => {
      const s = stockOf(product);
      return s && s.available === 0 ? s : null;
    }, 30), { 'some da venda em segundos': (s) => s.available === 0 });

    const customer = newCustomer(IDENTITY, 'e2e-esgotado-fornecedor');
    const orderId = placeOrder(product, customer);
    check(finalOrder(orderId, customer), {
      'pedido cancelled': (o) => o.status === 'cancelled',
      'motivo out_of_stock': (o) => o.cancellation_reason === 'out_of_stock',
    });
    const charges = http.get(`${GATEWAY}/payments/v1/payments?order_id=${orderId}`).json('data');
    check(charges, { 'recusado antes de cobrar': (d) => d.length === 0 });
    set(50); // devolve o estoque para a próxima rodada
  });

  group('6. PIX', () => {
    const customer = newCustomer(IDENTITY, 'e2e-pix');
    const orderId = placeOrder(products.pix, customer, { method: 'pix' });
    const pending = eventually('o QR do PIX no pedido', () => {
      const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
      return res.status === 200 && res.json('payment.pix') ? res.json() : null;
    });
    check(pending, {
      'pedido pending com o QR': (o) => o.status === 'pending' && o.payment.pix.qr_code.startsWith('000201'),
      'PIX com validade': (o) => Date.parse(o.payment.pix.expires_at) > Date.now(),
    });
    const payment = paymentOf(orderId, 'pending');
    // o "banco" do cliente paga o QR (a página do ticket_url faz o mesmo)
    const paid = http.post(`${PAYMENT_SIM}/sim/payments/${payment.provider_reference}/pay`, null, { tags: { name: 'sim-pay' } });
    check(paid, { 'PIX pago no banco': (r) => r.status === 200 });
    check(finalOrder(orderId, customer), { 'pedido confirmed': (o) => o.status === 'confirmed' });
    check(paymentOf(orderId, 'approved'), { 'cobrança approved': (p) => p.method === 'pix' && p.fee_cents > 0 });
  });

  group('7. nota fiscal recusada', () => {
    const customer = newCustomer(IDENTITY, 'e2e-nota', 'recusar-nota');
    const orderId = placeOrder(products.invoiceRejected, customer);
    // pago, a SEFAZ simulada rejeita a nota: o pedido não vai ao fornecedor e o cliente é estornado
    const order = eventually('pedido cancelado e estornado', () => {
      const res = http.get(`${GATEWAY}/orders/v1/orders/${orderId}`, { headers: auth(customer.token), tags: { name: 'order-status' } });
      return res.status === 200 && res.json('refunded_at') ? res.json() : null;
    });
    check(order, {
      'pedido cancelled': (o) => o.status === 'cancelled',
      'motivo invoice_failed': (o) => o.cancellation_reason === 'invoice_failed',
      'sem nota no pedido': (o) => o.invoice === null,
      'não foi ao fornecedor': (o) => !o.sent_to_supplier_at,
    });
    check(paymentOf(orderId, 'voided'), { 'autorização cancelada': (p) => p.status === 'voided' });
  });
}
