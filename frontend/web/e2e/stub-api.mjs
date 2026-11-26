// API falsa para os testes de navegador e o Lighthouse: responde como o gateway (/identity, /catalog)
// o suficiente para o BFF. Guarda o estado em memória e expõe contadores em /__stats.
import { createServer } from "node:http";

const port = Number(process.env.STUB_PORT ?? 4010);
const sessions = new Map(); // refresh token -> { email, used }
const accessTokens = new Map(); // access token -> email
// ordersByKey: pedidos criados por Idempotency-Key (o teste do envio duplo olha só a dele)
const stats = { refresh: 0, revoke: 0, ordersPlaced: 0, ordersByKey: {} };
let seq = 0;
const accounts = new Map(); // email -> { password, name }, das contas criadas no teste
const usedResetTokens = new Set(); // os links de "esqueci minha senha" já usados
const guests = new Map(); // email -> nome, de quem comprou sem conta
const guestKeys = new Map(); // chave do formulário -> email
const guestIds = new Map(); // email -> id da conta de convidado (cada compra sem conta é uma conta)
// a verificação em duas etapas da equipe (ameaça T8): todo "admin@..." entra com a senha e o código do app.
// "admin@novo..." ainda não tem o app: o login leva ao cadastro do autenticador
const mfaTickets = new Map(); // tíquete -> { email, status, used }
const usedRecoveryCodes = new Set();
const ADMIN_TOTP = "123456"; // o código que o "app autenticador" mostra
const RECOVERY_CODES = Array.from({ length: 10 }, (_, i) => `rcod${i}-k7m2p`);

// a entrega dos pedidos da Ana (#221): o checkout dela já vem preenchido com ela
const ANA_DELIVERY = {
  recipient: { name: "Ana Souza", cpf: "52998224725", phone: "11987654321" },
  shipping_address: {
    zip_code: "01310100",
    street: "Avenida Paulista",
    number: "1000",
    complement: "Apto 12",
    district: "Bela Vista",
    city: "São Paulo",
    state: "SP",
  },
};

// pedidos da Ana: um confirmado, um cancelado por falta de estoque, um entregue há 2 dias (#223) e um a
// caminho, com o rastreio (#224)
const orderOf = (id, status, reason, qty, unit) => ({
  id,
  customer_id: "0199f6a0-0000-7000-8000-000000000a1a",
  status,
  cancellation_reason: reason,
  items: [
    {
      product_id: "0199f6a0-0000-7000-8000-000000000000",
      sku: "SKU-0",
      name: 'Smart TV 55" QLED',
      quantity: qty,
      unit_price: { amount_cents: unit, currency: "BRL" },
      line_total: { amount_cents: unit * qty, currency: "BRL" },
      campaign_id: null,
    },
  ],
  items_total: { amount_cents: unit * qty, currency: "BRL" },
  shipping: null,
  total: { amount_cents: unit * qty, currency: "BRL" },
  payment_id: null,
  payment: { method: "card", installments: 1, pix: null },
  ...ANA_DELIVERY,
  created_at: "2026-10-20T15:00:00Z",
  updated_at: "2026-10-20T15:01:00Z",
});
const orders = [
  // o confirmado já tem a nota fiscal (#221)
  {
    ...orderOf("01a18d66-0000-7000-8000-00004f2a9c1b", "confirmed", null, 1, 263992),
    invoice: {
      access_key: "31261011222333000181550010000010011000010015",
      number: 1001,
      series: 1,
      issued_at: "2026-10-20T15:00:30Z",
      cancelled_at: null,
    },
  },
  orderOf("01a18d66-0000-7000-8000-00007c3e5d2a", "cancelled", "out_of_stock", 2, 263992),
  {
    ...orderOf("01a18d66-0000-7000-8000-00009d1b6e3f", "delivered", null, 1, 263992),
    delivered_at: new Date(Date.now() - 2 * 86_400_000).toISOString(),
    returnable_until: new Date(Date.now() + 5 * 86_400_000).toISOString(),
    return_requested_at: null,
  },
  // entregue há 10 dias: o prazo de 7 dias para desistir já passou
  {
    ...orderOf("01a18d66-0000-7000-8000-00005b7c1d90", "delivered", null, 1, 263992),
    delivered_at: new Date(Date.now() - 10 * 86_400_000).toISOString(),
    returnable_until: new Date(Date.now() - 3 * 86_400_000).toISOString(),
    return_requested_at: null,
    tracking: {
      number: "BR111222333BR",
      provider: "Correios",
      url: "https://rastreio.example/BR111222333BR",
    },
  },
  {
    ...orderOf("01a18d66-0000-7000-8000-00002e6f8a14", "shipped", null, 1, 263992),
    sent_to_supplier_at: "2026-10-20T15:02:00Z",
    shipped_at: "2026-10-21T10:00:00Z",
    tracking: {
      number: "BR987654321BR",
      provider: "Correios",
      url: "https://rastreio.example/BR987654321BR",
    },
  },
];

const categories = [
  { id: "0199f6a0-0000-7000-8000-0000000000a0", slug: "eletronicos", name: "Eletrônicos", parent_id: null },
  {
    id: "0199f6a0-0000-7000-8000-0000000000aa",
    slug: "tv",
    name: "Televisores",
    parent_id: "0199f6a0-0000-7000-8000-0000000000a0",
  },
  {
    id: "0199f6a0-0000-7000-8000-0000000000ab",
    slug: "fones",
    name: "Fones de ouvido",
    parent_id: "0199f6a0-0000-7000-8000-0000000000a0",
  },
  // os tipos de fones (#210): o terceiro nível
  {
    id: "0199f6a0-0000-7000-8000-0000000000ac",
    slug: "fones-sem-fio",
    name: "Fones sem fio",
    parent_id: "0199f6a0-0000-7000-8000-0000000000ab",
  },
  {
    id: "0199f6a0-0000-7000-8000-0000000000ad",
    slug: "headsets-gamer",
    name: "Headsets gamer",
    parent_id: "0199f6a0-0000-7000-8000-0000000000ab",
  },
  { id: "0199f6a0-0000-7000-8000-0000000000b0", slug: "informatica", name: "Informática", parent_id: null },
  {
    id: "0199f6a0-0000-7000-8000-0000000000bb",
    slug: "notebooks",
    name: "Notebooks",
    parent_id: "0199f6a0-0000-7000-8000-0000000000b0",
  },
  // sem nenhum produto à venda (#483): fora do menu, da home, do sitemap, e a página é 404
  { id: "0199f6a0-0000-7000-8000-0000000000c0", slug: "brinquedos", name: "Brinquedos", parent_id: null },
];
const bySlug = Object.fromEntries(categories.map((c) => [c.slug, c]));

// a campanha termina sempre daqui a 3 dias: a contagem regressiva nunca chega a zero no teste
const endsAt = new Date(Date.now() + 3 * 86400_000).toISOString();
const campaign = {
  id: "0199f6a0-0000-7000-8000-0000000000c1",
  name: "Black Friday",
  discount_percent: 20,
  ends_at: endsAt,
};
const campaigns = [
  { ...campaign, slug: "black-friday", starts_at: "2026-10-01T00:00:00Z", product_count: 2 },
];

const seed = [
  ['Smart TV 55" QLED', "tv", 329990, 263992, "Samsung"],
  ["Fone Bluetooth com cancelamento de ruído", "fones-sem-fio", 49990, 49990, "Sony"],
  ["Notebook Core i7 16GB SSD 512GB", "notebooks", 549990, 439992, "Dell"],
];
// 60 fones a mais: Eletrônicos passa de um lote (48) e exercita o "Carregar mais"; três marcas
const fones = ["JBL", "Edifier", "Xiaomi"];
for (let i = 1; i <= 60; i++)
  seed.push([`Fone de ouvido modelo ${i}`, "fones", 9990 + i * 100, 9990 + i * 100, fones[i % 3]]);
// um headset gamer no fim (os índices de cima são usados pelo estoque): a faceta de tipos tem dois
seed.push(["Headset gamer 7.1", "headsets-gamer", 29990, 29990, "HyperX"]);

const products = seed.map(([name, slug, price, effective, brand], i) => {
  const { id, name: categoryName } = bySlug[slug];
  const category = { id, slug, name: categoryName };
  return {
    id: `0199f6a0-0000-7000-8000-${String(i).padStart(12, "0")}`,
    sku: `SKU-${i}`,
    name,
    description: "",
    brand,
    category,
    image_url: null,
    image: null,
    price: { amount_cents: price, currency: "BRL" },
    effective_price: { amount_cents: effective, currency: "BRL" },
    campaign: effective < price ? campaign : null,
    sizes: [],
    colors: [],
    personalization: null,
    created_at: "2026-10-01T10:00:00Z",
    updated_at: "2026-10-01T10:00:00Z",
  };
});

// as fotos da TV (0), para a galeria: quadradas, cada uma de uma cor
const photo = (color) => ({
  url: `data:image/svg+xml,<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" fill="${color}"/></svg>`,
  width: 800,
  height: 800,
  variants: [],
});
products[0].images = ["steelblue", "goldenrod", "seagreen"].map(photo);
products[0].image = products[0].images[0];
products[0].image_url = products[0].images[0].url;
// um fone (10) com tamanho, para o seletor da página e as linhas por tamanho no carrinho
products[10].sizes = ["P", "M", "G"];
// um fone (11) com o nome do cliente na arte, obrigatório, até 12 caracteres
products[11].personalization = {
  label: "Nome",
  max_length: 12,
  required: true,
  template: "nome-classico:mae",
};
// um fone (12) com cores à escolha
products[12].colors = ["Branco", "Preto", "Vinho"];
// uma foto por cor, na ordem das cores: a galeria acompanha a cor escolhida
products[12].images = ["whitesmoke", "black", "maroon"].map(photo);
products[12].image = products[12].images[0];
products[12].image_url = products[12].images[0].url;

// o notebook (2) tem só 3 unidades e o último fone (32) esgotou: exercitam os limites do carrinho
const baseStock = (id) => ({ [products[2].id]: 3, [products[32].id]: 0 })[id] ?? 100;
// ajustes feitos pelo painel, por produto
const adjustments = new Map();
const stock = (id) => baseStock(id) + (adjustments.get(id) ?? 0);

// o painel: situação e versão de cada produto (a versão é o ETag)
// o código na Montink (#483): um só, sem cor; o do feed de dropshipping não precisa
for (const [i, p] of products.entries()) {
  Object.assign(p, {
    status: "active",
    version: 1,
    supplier: null,
    supplier_products: { "": `61${1000 + i}` },
  });
}
// o headset vem do fornecedor de dropshipping (#217)
products.at(-1).supplier = {
  name: "cj",
  vid: "SIM-V-004",
  cost: { amount_cents: 1890, currency: "USD" },
  synced_at: "2026-10-29T12:00:00Z",
  removed_at: null,
};
// regras de margem do painel (#217): a padrão começa como a da configuração (updated_at nulo)
const pricing = { default: { margin_percent: 80, min_price_cents: 1990, updated_at: null }, rules: [] };
const adminKeys = new Set(); // Idempotency-Key dos ajustes de estoque
const adminCampaigns = [];
// dead letter topics: o inventory com duas mensagens; orders e notifications vazios
const poison = (offset) => ({
  event_id: null,
  event_type: null,
  topic: "orders.order.v1",
  partition: 0,
  offset,
  attempts: 1,
  error: "PoisonMessageException: mensagem sem envelope válido",
  failed_at: "2026-10-27T12:00:00Z",
});
const deadLetterQueues = {
  inventory: [
    poison(7150),
    {
      ...poison(7151),
      event_id: "0199f6a0-0000-7000-8000-0000000000d1",
      event_type: "OrderPlaced",
      attempts: 4,
      error: "TimeoutException: banco do inventory não respondeu",
    },
  ],
  orders: [],
  notifications: [],
  fulfillment: [],
  catalog: [],
}; // campanhas criadas pelo painel nos testes (não mudam o preço da vitrine)
// os pedidos que o painel lista (#382)
const adminOrder = (
  id,
  status,
  reason,
  name,
  email,
  phone,
  city,
  state,
  method,
  installments,
  cents,
  created,
  extra = {},
) => ({
  id,
  status,
  cancellation_reason: reason,
  created_at: created,
  total: { amount_cents: cents, currency: "BRL" },
  items_count: 1,
  payment: { method, installments },
  customer: { email, name, phone },
  destination: city ? { city, state } : null,
  items: [
    {
      product_id: "0199f6a0-0000-7000-8000-000000000001",
      sku: "FONE-1",
      name: "Fone Sonix",
      quantity: 1,
      unit_price: { amount_cents: cents - 1500, currency: "BRL" },
      line_total: { amount_cents: cents - 1500, currency: "BRL" },
      campaign_id: null,
    },
  ],
  items_total: { amount_cents: cents - 1500, currency: "BRL" },
  shipping: {
    service: "economico",
    name: "Econômico",
    price: { amount_cents: 1500, currency: "BRL" },
    min_days: 3,
    max_days: 7,
    promised_by: "2026-11-12",
  },
  tracking: null,
  invoice_number: null,
  updated_at: created,
  ...extra,
});
const adminOrders = [
  adminOrder(
    "01a18d66-0000-7000-8000-0000b1000001",
    "shipped",
    null,
    "Bia Lima",
    "bia@exemplo.com",
    "35999990000",
    "Lavras",
    "MG",
    "card",
    3,
    21500,
    "2026-11-03T15:00:00Z",
    {
      sent_to_supplier_at: "2026-11-03T16:00:00Z",
      shipped_at: "2026-11-04T10:00:00Z",
      tracking: { number: "BR123", provider: "Correios", url: null },
    },
  ),
  adminOrder(
    "01a18d66-0000-7000-8000-0000b2000002",
    "cancelled",
    "supplier_failed",
    "Caio Souza",
    "caio@exemplo.com",
    null,
    "Recife",
    "PE",
    "pix",
    1,
    9900,
    "2026-11-02T12:00:00Z",
    {
      refunded_at: "2026-11-02T18:00:00Z",
    },
  ),
  adminOrder(
    "01a18d66-0000-7000-8000-0000b3000003",
    "delivered",
    null,
    null,
    "antigo@exemplo.com",
    null,
    null,
    null,
    "pix",
    1,
    5000,
    "2026-10-28T09:00:00Z",
    {
      delivered_at: "2026-11-01T14:00:00Z",
    },
  ),
  // o estorno do PIX que a Efí aceitou e não realizou, sem saldo na conta (#396)
  adminOrder(
    "01a18d66-0000-7000-8000-0000b4000004",
    "cancelled",
    "supplier_failed",
    "Duda Reis",
    "duda@exemplo.com",
    null,
    "Lavras",
    "MG",
    "pix",
    1,
    7900,
    "2026-11-04T09:00:00Z",
    {
      refunded_at: null,
      refund_failed_at: "2026-11-04T12:00:00Z",
    },
  ),
];
/** Os estornos pedidos de novo pelo painel (#396), pela chave. */
const refundsAgain = new Set();

const isAdmin = (req) =>
  (accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", "")) ?? "").startsWith("admin@");

/** Como o catalog: à venda só com o código no fornecedor (o do feed de dropshipping tem o dele). */
function withoutSupplier(res, body, p) {
  const codes = body.supplier_products ?? p?.supplier_products ?? {};
  if (body.status !== "active" || p?.supplier || Object.keys(codes).length > 0) return false;
  send(
    res,
    422,
    {
      type: "about:blank",
      title: "Dados inválidos",
      status: 422,
      errors: [
        {
          field: "supplier_products",
          message: "para pôr à venda, informe o código do produto no fornecedor (Montink)",
        },
      ],
    },
    "application/problem+json",
  );
  return true;
}

/** Corpo do ProductInput aplicado ao produto (o preço novo vale para o efetivo sem campanha). */
function applyInput(p, body) {
  const category = categories.find((c) => c.id === body.category_id) ?? p.category;
  Object.assign(p, {
    sku: body.sku,
    name: body.name,
    description: body.description ?? "",
    brand: body.brand ?? null,
    image_url: body.image_url ?? null,
    images: [body.image_url, ...(body.more_image_urls ?? [])].filter(Boolean).map((url) => ({
      url,
      width: null,
      height: null,
      variants: [],
    })),
    status: body.status,
    supplier_products: body.supplier_products ?? p.supplier_products ?? {},
    category: { id: category.id, slug: category.slug, name: category.name },
    price: { amount_cents: body.price_cents, currency: "BRL" },
    effective_price: {
      amount_cents: p.campaign
        ? Math.floor((body.price_cents * (100 - p.campaign.discount_percent)) / 100)
        : body.price_cents,
      currency: "BRL",
    },
    updated_at: new Date().toISOString(),
  });
}

async function admin(req, res, url) {
  if (!isAdmin(req)) return problem(res, req.headers.authorization ? 403 : 401, "Sem permissão");
  const one = url.pathname.match(/^\/catalog\/v1\/admin\/products\/([0-9a-f-]{36})$/);
  if (one) {
    const p = products.find((x) => x.id === one[1]);
    if (!p) return problem(res, 404, "Recurso não encontrado");
    if (req.method === "GET") {
      res.setHeader("ETag", `"${p.version}"`);
      return send(res, 200, p);
    }
    if (req.method === "PUT") {
      const body = await json(req);
      if (req.headers["if-match"] !== `"${p.version}"`) return problem(res, 412, "Versão desatualizada");
      if (products.some((x) => x.id !== p.id && x.sku === body.sku)) return problem(res, 409, "Conflito");
      if (withoutSupplier(res, body, p)) return;
      applyInput(p, body);
      p.version++;
      res.setHeader("ETag", `"${p.version}"`);
      return send(res, 200, p);
    }
    if (req.method === "DELETE") {
      products.splice(products.indexOf(p), 1);
      return res.writeHead(204).end();
    }
  }
  const adjust = url.pathname.match(/^\/inventory\/v1\/admin\/stock\/([0-9a-f-]{36})\/adjustments$/);
  if (adjust && req.method === "POST") {
    const { delta } = await json(req);
    const key = `${adjust[1]}:${req.headers["idempotency-key"]}`;
    if (!adminKeys.has(key)) {
      if (stock(adjust[1]) + delta < 0) return problem(res, 409, "Conflito");
      adminKeys.add(key);
      adjustments.set(adjust[1], (adjustments.get(adjust[1]) ?? 0) + delta);
    }
    const available = stock(adjust[1]);
    return send(res, 200, {
      product_id: adjust[1],
      on_hand: available,
      reserved: 0,
      available,
      updated_at: new Date().toISOString(),
    });
  }
  if (req.method === "GET" && url.pathname === "/catalog/v1/admin/products") {
    const q = url.searchParams.get("q")?.toLowerCase();
    const status = url.searchParams.get("status");
    const data = products.filter(
      (p) =>
        (!q || `${p.name} ${p.brand} ${p.sku}`.toLowerCase().includes(q)) && (!status || p.status === status),
    );
    return send(res, 200, { data: data.slice(0, 50), page: { next_cursor: null, has_more: false } });
  }
  if (req.method === "POST" && url.pathname === "/catalog/v1/admin/products") {
    const body = await json(req);
    if (products.some((x) => x.sku === body.sku)) return problem(res, 409, "Conflito");
    if (!/^[A-Z0-9-]{3,32}$/.test(body.sku)) {
      return send(
        res,
        422,
        {
          type: "about:blank",
          title: "Dados inválidos",
          status: 422,
          errors: [{ field: "sku", message: "use letras maiúsculas" }],
        },
        "application/problem+json",
      );
    }
    if (withoutSupplier(res, body, null)) return;
    const p = {
      id: crypto.randomUUID(),
      campaign: null,
      image: null,
      supplier: null,
      version: 1,
      created_at: new Date().toISOString(),
    };
    applyInput(p, body);
    products.push(p);
    res.setHeader("ETag", '"1"');
    return send(res, 201, p);
  }
  // ---- campanhas: um produto fica em uma campanha por período ----
  const campaignPath = url.pathname.match(/^\/catalog\/v1\/admin\/campaigns\/([0-9a-f-]{36})(\/products)?$/);
  const overlaps = (a, b) =>
    Date.parse(a.starts_at) < Date.parse(b.ends_at) && Date.parse(b.starts_at) < Date.parse(a.ends_at);
  const withCount = (c) => ({ ...c, product_count: c.products.length, products: undefined });
  if (url.pathname === "/catalog/v1/admin/campaigns") {
    if (req.method === "GET") {
      return send(res, 200, {
        data: adminCampaigns.map(withCount),
        page: { next_cursor: null, has_more: false },
      });
    }
    const body = await json(req);
    if (adminCampaigns.some((c) => c.slug === body.slug)) return problem(res, 409, "Conflito");
    const c = { id: crypto.randomUUID(), ...body, products: [] };
    adminCampaigns.push(c);
    return send(res, 201, withCount(c));
  }
  if (campaignPath) {
    const c = adminCampaigns.find((x) => x.id === campaignPath[1]);
    if (!c) return problem(res, 404, "Recurso não encontrado");
    if (campaignPath[2]) {
      if (req.method === "GET")
        return send(res, 200, {
          data: c.products.map((id) => products.find((p) => p.id === id)).filter(Boolean),
        });
      const { product_ids } = await json(req);
      const busy = adminCampaigns.some(
        (o) => o.id !== c.id && overlaps(o, c) && o.products.some((id) => product_ids.includes(id)),
      );
      if (busy) {
        return send(
          res,
          409,
          {
            type: "about:blank",
            title: "Conflito",
            status: 409,
            detail: "produtos já estão em outra campanha no período",
            trace_id: "4bf92f3577b34da6a3ce929d0e0e4736",
          },
          "application/problem+json",
        );
      }
      c.products = product_ids;
      return res.writeHead(204).end();
    }
    if (req.method === "GET") return send(res, 200, withCount(c));
    if (req.method === "PUT") {
      Object.assign(c, await json(req));
      return send(res, 200, withCount(c));
    }
    if (req.method === "DELETE") {
      adminCampaigns.splice(adminCampaigns.indexOf(c), 1);
      return res.writeHead(204).end();
    }
  }
  if (req.method === "POST" && url.pathname === "/catalog/v1/admin/categories") {
    const body = await json(req);
    if (categories.some((c) => c.slug === body.slug)) return problem(res, 409, "Conflito");
    const c = {
      id: crypto.randomUUID(),
      slug: body.slug,
      name: body.name,
      parent_id: body.parent_id ?? null,
    };
    categories.push(c);
    bySlug[c.slug] = c;
    return send(res, 201, c);
  }
  return problem(res, 404, "Recurso não encontrado");
}

/** Os slugs de uma categoria e de todas as descendentes. */
function subtree(id) {
  const root = categories.find((c) => c.id === id);
  if (!root) return [];
  return [root.slug, ...categories.filter((c) => c.parent_id === id).flatMap((c) => subtree(c.id))];
}

/** Os produtos que passam nos filtros da vitrine (sem a marca, para a faceta). */
function filtered(url) {
  const q = url.searchParams.get("q")?.toLowerCase();
  const slug = url.searchParams.get("category");
  // a categoria e a subárvore dela, como o catalog (WITH RECURSIVE)
  let family = slug ? subtree(bySlug[slug]?.id) : null;
  // os tipos (#212): a união das subárvores, só dentro da categoria
  const types = (url.searchParams.get("type") ?? "").split(",").filter(Boolean);
  if (types.length) {
    const within = types.flatMap((t) => subtree(bySlug[t]?.id));
    family = family ? within.filter((s) => family.includes(s)) : within;
  }
  const min = Number(url.searchParams.get("min_price_cents") ?? 0);
  const max = Number(url.searchParams.get("max_price_cents") ?? Infinity);
  return products.filter(
    (p) =>
      (!q || p.name.toLowerCase().includes(q) || p.brand.toLowerCase().includes(q)) &&
      (!family || family.includes(p.category.slug)) &&
      (url.searchParams.get("on_sale") !== "true" || p.campaign) &&
      p.effective_price.amount_cents >= min &&
      p.effective_price.amount_cents <= max,
  );
}

const sorters = {
  price_asc: (a, b) => a.effective_price.amount_cents - b.effective_price.amount_cents,
  price_desc: (a, b) => b.effective_price.amount_cents - a.effective_price.amount_cents,
  discount: (a, b) => (b.campaign?.discount_percent ?? 0) - (a.campaign?.discount_percent ?? 0),
};

/** GET /v1/products com os filtros e as ordens da vitrine; o cursor é o índice do próximo item. */
function listProducts(url) {
  const brands = (url.searchParams.get("brand") ?? "").toLowerCase().split(",").filter(Boolean);
  const matches = filtered(url).filter((p) => !brands.length || brands.includes(p.brand.toLowerCase()));
  const sorter = sorters[url.searchParams.get("sort")];
  if (sorter) matches.sort(sorter);
  const limit = Number(url.searchParams.get("limit") ?? 20);
  // por número (?page=, com o total) ou pelo cursor (o índice do próximo item)
  const page = Number(url.searchParams.get("page") ?? 0);
  const start = page > 0 ? (page - 1) * limit : Number(url.searchParams.get("cursor") ?? 0);
  const data = matches.slice(start, start + limit);
  const more = start + limit < matches.length;
  if (page > 0) return { data, page: { next_cursor: null, has_more: more }, total: matches.length };
  return { data, page: { next_cursor: more ? String(start + limit) : null, has_more: more } };
}

/** GET /v1/brands: as marcas da listagem com a contagem, das mais frequentes para as menos. */
/** A faceta de tipos: as filhas da categoria com quantos produtos (sem o filtro de tipo, com a marca). */
function listCategoryCounts(url) {
  const root = bySlug[url.searchParams.get("category")];
  const noTypes = new URL(url);
  noTypes.searchParams.delete("type");
  const brands = (url.searchParams.get("brand") ?? "").toLowerCase().split(",").filter(Boolean);
  const list = filtered(noTypes).filter((p) => !brands.length || brands.includes(p.brand.toLowerCase()));
  return {
    data: categories
      .filter((c) => root && c.parent_id === root.id)
      .map((c) => ({
        slug: c.slug,
        name: c.name,
        count: list.filter((p) => subtree(c.id).includes(p.category.slug)).length,
      }))
      .filter((c) => c.count > 0)
      .sort((a, b) => a.name.localeCompare(b.name)),
  };
}

function listBrands(url) {
  const counts = new Map();
  for (const p of filtered(url)) counts.set(p.brand, (counts.get(p.brand) ?? 0) + 1);
  return {
    data: [...counts]
      .sort((a, b) => b[1] - a[1] || a[0].localeCompare(b[0]))
      .map(([name, count]) => ({ name, count })),
  };
}

// ---- pedidos feitos no teste: a SAGA anda sozinha com o tempo ----
const placed = new Map(); // id -> { order, reserveAt, endAt, outcome }
const byKey = new Map(); // `${email}:${Idempotency-Key}` -> id
const TRACE = "4bf92f3577b34da6a3ce929d0e0e4736";
const ANA = "0199f6a0-0000-7000-8000-000000000a1a";

/**
 * O pedido no instante `now`: reservando, cobrando e, no fim, confirmado ou cancelado. No PIX, a cobrança
 * gera o código e espera: o teste paga (POST /__pix/{id}/pay) ou deixa vencer.
 */
function orderAt(entry, now = Date.now()) {
  const { order, reserveAt, endAt, outcome } = entry;
  if (outcome === "out_of_stock" && now >= reserveAt) {
    return { ...order, status: "cancelled", cancellation_reason: "out_of_stock", updated_at: iso(reserveAt) };
  }
  const reserved = now >= reserveAt ? { stock_reserved_at: iso(reserveAt), updated_at: iso(reserveAt) } : {};
  if (order.payment.method === "pix" && now >= endAt) {
    const pix = {
      qr_code: `00020101021226820014br.gov.bcb.pix2560pix.example/qr/${order.id}5204000053039865802BR6304ABCD`,
      qr_code_base64: QR_PNG,
      ticket_url: `http://localhost:${port}/sim/pix/${order.id}`,
      expires_at: iso(endAt + entry.pixMs),
    };
    const waiting = { ...order, ...reserved, payment: { ...order.payment, pix }, updated_at: iso(endAt) };
    if (entry.paidAt) {
      return {
        ...waiting,
        status: "confirmed",
        payment_id: crypto.randomUUID(),
        updated_at: iso(entry.paidAt),
      };
    }
    if (now >= endAt + entry.pixMs) {
      return {
        ...waiting,
        status: "cancelled",
        cancellation_reason: "payment_failed",
        updated_at: iso(endAt + entry.pixMs),
      };
    }
    return waiting;
  }
  if (now < endAt) return { ...order, ...reserved };
  return outcome === "payment_failed"
    ? {
        ...order,
        ...reserved,
        status: "cancelled",
        cancellation_reason: "payment_failed",
        updated_at: iso(endAt),
      }
    : { ...order, ...reserved, status: "confirmed", payment_id: crypto.randomUUID(), updated_at: iso(endAt) };
}
const iso = (ms) => new Date(ms).toISOString();
// um PNG de 1×1: o teste confere que o QR aparece, não que ele é lido
const QR_PNG =
  "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg==";

// ---- o frete (#222): duas opções para qualquer CEP, menos o 99999-999, que ninguém atende. O 01310-200 cota
// devagar, para o clique em "Fazer o pedido" chegar antes do frete (#331) ----
const SHIPPING = [
  { id: "economico", name: "Econômico", price_cents: 2430, min_days: 13, max_days: 21 },
  { id: "expresso", name: "Expresso", price_cents: 6480, min_days: 7, max_days: 11 },
];
// o frete grátis pela margem (#323): o headset vem do fornecedor e tem margem para pagar o econômico. Como no
// fulfillment, só vale com o product_id de todos os itens
const FREE_SHIPPING_PRODUCT = "0199f6a0-0000-7000-8000-000000000063";
const shippingFor = (zip, productIds = []) => {
  if (zip === "99999999") return [];
  const free = productIds.length > 0 && productIds.every((id) => id === FREE_SHIPPING_PRODUCT);
  return SHIPPING.map((o) =>
    free && o.id === "economico" ? { ...o, price_cents: 0, regular_price_cents: o.price_cents } : o,
  );
};

// ---- o provedor de pagamento: o navegador tokeniza o cartão aqui (outra origem, com CORS) ----
const cardTokens = new Map(); // payment_token -> número do cartão
const CORS = {
  "Access-Control-Allow-Origin": "*",
  "Access-Control-Allow-Methods": "POST, OPTIONS",
  "Access-Control-Allow-Headers": "Content-Type",
};
async function tokenize(req, res) {
  for (const [k, v] of Object.entries(CORS)) res.setHeader(k, v);
  if (req.method === "OPTIONS") return res.writeHead(204).end();
  // o /sim/card-token do payment-sim, no lugar da biblioteca da Efí (#379)
  const body = await json(req);
  if (body.payee_code !== "0123456789abcdef0123456789abcdef")
    return send(res, 401, {
      code: 3500008,
      error: "invalid_request",
      error_description: "Identificador de conta inválido.",
    });
  const number = String(body.number ?? "");
  if (!/^4\d{15}$|^5\d{15}$/.test(number)) {
    return send(res, 400, {
      code: 3500057,
      error: "invalid_card_number",
      error_description: "O número do cartão é inválido.",
    });
  }
  const token = crypto.randomUUID().replace(/-/g, "") + "00000000";
  cardTokens.set(token, number);
  return send(res, 200, {
    code: 200,
    data: { payment_token: token, card_mask: "XXXXXXXXXXXX" + number.slice(-4) },
  });
}

// as devoluções ao fornecedor (#223): uma recusada, com a nota rejeitada, uma parada e uma no prazo
const REFUSED_AT_OPENING = "01a18d66-0000-7000-8000-00009d4e5bc3";
// o que a equipe registrou nas recusadas na abertura, por pedido
const returnInstructions = new Map();

function supplierReturns() {
  const day = 24 * 60 * 60 * 1000;
  const now = Date.now();
  const base = {
    supplier: "cj",
    supplier_order_id: "CJ2410290001",
    items: [{ sku: "SIM-V-001", name: "Fone de ouvido", quantity: 1 }],
    attempts: 0,
    last_error: null,
    resolution_reason: null,
    invoice_error: null,
    resolved_at: null,
    return_address: null,
    awaiting_customer_instruction: false,
    customer_instruction: null,
    instructed_at: null,
  };
  // a recusada na abertura (#281): espera a instrução até a equipe registrar
  const refused = returnInstructions.get(REFUSED_AT_OPENING);
  return [
    {
      ...base,
      order_id: REFUSED_AT_OPENING,
      status: "rejected",
      overdue: false,
      due_at: iso(now + 14 * day),
      dispute_id: null,
      return_code: refused?.return_code ?? null,
      return_address: refused?.return_address ?? null,
      customer_email: "edu@exemplo.com",
      resolution_reason: "Return refused: outside the supplier policy",
      requested_at: iso(now - 1 * day),
      opened_at: null,
      resolved_at: iso(now - 1 * day),
      awaiting_customer_instruction: !refused,
      customer_instruction: refused?.instruction ?? null,
      instructed_at: refused?.at ?? null,
    },
    {
      ...base,
      order_id: "01a18d66-0000-7000-8000-00007a1c2e90",
      status: "pending",
      overdue: true,
      due_at: iso(now - 2 * day),
      dispute_id: null,
      return_code: null,
      customer_email: "bia@exemplo.com",
      attempts: 3,
      last_error: "fornecedor respondeu 503",
      requested_at: iso(now - 17 * day),
      opened_at: null,
    },
    {
      ...base,
      order_id: "01a18d66-0000-7000-8000-00008b2d3fa1",
      status: "open",
      overdue: false,
      due_at: iso(now + 10 * day),
      dispute_id: "DSP1000000002",
      return_code: "RMA00000002",
      customer_email: "caio@exemplo.com",
      requested_at: iso(now - 5 * day),
      opened_at: iso(now - 5 * day),
    },
    {
      ...base,
      order_id: "01a18d66-0000-7000-8000-00006c3e4ab2",
      status: "rejected",
      overdue: false,
      due_at: iso(now + 12 * day),
      dispute_id: "DSP1000000003",
      return_code: "RMA00000003",
      customer_email: "dani@exemplo.com",
      resolution_reason: "produto fora da política de devolução do fornecedor",
      invoice_error: "267: sem referência",
      requested_at: iso(now - 3 * day),
      opened_at: iso(now - 3 * day),
      resolved_at: iso(now - 1 * day),
    },
  ];
}

// os produtos com a API fora (POST /__teste/fora/{id}), para a reserva da vitrine
const productsDown = new Set();

// os pedidos atrasados no fornecedor (#219): um preso no fornecedor e um envio que não chegou
function overdueOrders() {
  const day = 24 * 60 * 60 * 1000;
  const now = Date.now();
  return [
    {
      order_id: "01a18d66-0000-7000-8000-00005b8e1f20",
      status: "placed",
      stage: "shipping",
      due_at: iso(now - 4 * day),
      supplier: "cj",
      supplier_order_id: "CJ2410290001",
      customer_email: "bia@exemplo.com",
      attempts: 0,
      last_error: null,
      tracking: null,
      created_at: iso(now - 7 * day),
      placed_at: iso(now - 7 * day),
      shipped_at: null,
    },
    {
      order_id: "01a18d66-0000-7000-8000-00009d2c7a40",
      status: "shipped",
      stage: "delivery",
      due_at: iso(now - 5 * 60 * 60 * 1000),
      supplier: "cj",
      supplier_order_id: "CJ2410100002",
      customer_email: "caio@exemplo.com",
      attempts: 0,
      last_error: null,
      tracking: {
        number: "BR123456789CJ",
        provider: "Correios",
        url: "https://t.17track.net/en#nums=BR123456789CJ",
      },
      created_at: iso(now - 20 * day),
      placed_at: iso(now - 20 * day),
      shipped_at: iso(now - 15 * day - 5 * 60 * 60 * 1000),
    },
  ];
}
const eventId = (o) => String(Date.parse(o.updated_at));

async function placeOrder(req, res, email) {
  const key = req.headers["idempotency-key"];
  if (!key) return problem(res, 400, "Requisição malformada");
  const existing = byKey.get(`${email}:${key}`);
  const { items, payment, recipient, shipping_address, shipping } = await json(req);
  if (existing) return send(res, 202, orderAt(placed.get(existing)));
  // como o orders: cartão sem o token do provedor (ou com um token já usado) é recusado
  if (
    !payment ||
    !recipient?.cpf ||
    !/^\d{8}$/.test(shipping_address?.zip_code ?? "") ||
    !shippingFor(shipping_address?.zip_code).some((o) => o.id === shipping?.service) ||
    (payment.method === "card" && !cardTokens.has(payment.card_token))
  ) {
    return problem(res, 422, "Dados inválidos");
  }
  // "instavel@..." pega o catálogo fora e sem cópia local: 503 com Retry-After e o trace para o suporte
  if (email.startsWith("instavel@")) {
    res.setHeader("Retry-After", "7");
    return send(
      res,
      503,
      { type: "about:blank", title: "Serviço indisponível", status: 503, trace_id: TRACE },
      "application/problem+json",
    );
  }
  const lines = items.map((i) => ({ ...i, product: products.find((p) => p.id === i.product_id) }));
  // como o catalog: o produto com tamanho exige um dos dele, e o sem tamanho não aceita
  const sizeOk = (l) => (l.product.sizes.length ? l.product.sizes.includes(l.size) : !l.size);
  const colorOk = (l) => (l.product.colors.length ? l.product.colors.includes(l.color) : !l.color);
  // e a personalização: obrigatória quando o produto pede, até o máximo, e recusada no produto sem ela
  const textOk = (l) => {
    const rule = l.product.personalization;
    if (!rule) return !l.personalization;
    if (!l.personalization) return !rule.required;
    return [...l.personalization].length <= rule.max_length;
  };
  if (
    lines.some(
      (l) =>
        !l.product ||
        l.expected_unit_price_cents !== l.product.effective_price.amount_cents ||
        !sizeOk(l) ||
        !colorOk(l) ||
        !textOk(l),
    )
  ) {
    return send(
      res,
      409,
      { type: "about:blank", title: "Itens recusados pelo catálogo", status: 409, trace_id: TRACE },
      "application/problem+json",
    );
  }
  const orderItems = lines.map(({ product, quantity, size, color, personalization }) => ({
    size: size ?? null,
    color: color ?? null,
    personalization: personalization ?? null,
    product_id: product.id,
    sku: product.sku,
    name: product.name,
    quantity,
    unit_price: product.effective_price,
    line_total: { amount_cents: product.effective_price.amount_cents * quantity, currency: "BRL" },
    campaign_id: product.campaign?.id ?? null,
  }));
  const itemsTotal = orderItems.reduce((sum, i) => sum + i.line_total.amount_cents, 0);
  const option = shippingFor(
    shipping_address.zip_code,
    items.map((i) => i.product_id),
  ).find((o) => o.id === shipping.service);
  const total = itemsTotal + option.price_cents;
  const now = Date.now();
  const id = crypto.randomUUID();
  const order = {
    id,
    customer_id: ANA,
    status: "pending",
    cancellation_reason: null,
    items: orderItems,
    items_total: { amount_cents: itemsTotal, currency: "BRL" },
    shipping: {
      service: option.id,
      name: option.name,
      price: { amount_cents: option.price_cents, currency: "BRL" },
      min_days: option.min_days,
      max_days: option.max_days,
      promised_by: iso(now + option.max_days * 1.4 * 24 * 60 * 60 * 1000).slice(0, 10),
    },
    total: { amount_cents: total, currency: "BRL" },
    payment_id: null,
    payment: { method: payment.method, installments: payment.installments ?? 1, pix: null },
    recipient,
    shipping_address,
    stock_reserved_at: null,
    created_at: iso(now),
    updated_at: iso(now),
  };
  // o último dígito decide o cartão, como na homologação da Efí (final 1 recusa); o inventory recusa acima do saldo
  const cardNumber = payment.method === "card" ? (cardTokens.get(payment.card_token) ?? "") : "";
  cardTokens.delete(payment.card_token);
  const outcome = lines.some((l) => l.quantity > stock(l.product.id))
    ? "out_of_stock"
    : cardNumber.endsWith("1")
      ? "payment_failed"
      : "confirmed";
  // "pix-curto@...": o PIX vence em segundos, para o teste ver o prazo acabar
  const pixMs = email.startsWith("pix-curto@") ? 4_000 : 30 * 60_000;
  placed.set(id, { order, owner: email, reserveAt: now + 800, endAt: now + 1600, outcome, pixMs });
  byKey.set(`${email}:${key}`, id);
  stats.ordersPlaced++;
  stats.ordersByKey[key] = (stats.ordersByKey[key] ?? 0) + 1;
  return send(res, 202, order);
}

// como o orders: pago não é o fim (o pedido segue o fornecedor); o stub não passa do confirmado
const isFinal = (o) => o.status === "delivered" || o.status === "cancelled" || o.status === "returned";

/** GET .../events: o estado atual e cada mudança, em Server-Sent Events, até o estado final. */
function streamOrder(req, res, entry) {
  const last = req.headers["last-event-id"];
  let current = orderAt(entry);
  if (isFinal(current) && last === eventId(current)) return res.writeHead(204).end();
  res.writeHead(200, { "Content-Type": "text/event-stream", "Cache-Control": "no-cache" });
  res.write("retry: 3000\n\n");
  const emit = (o) => res.write(`id: ${eventId(o)}\nevent: order\ndata: ${JSON.stringify(o)}\n\n`);
  if (last !== eventId(current)) emit(current);
  const timer = setInterval(() => {
    const next = orderAt(entry);
    if (next.updated_at !== current.updated_at) {
      current = next;
      emit(current);
    }
    if (isFinal(current)) {
      clearInterval(timer);
      res.end();
    }
  }, 50);
  req.on("close", () => clearInterval(timer));
}

// ---- o Google de mentira: escolha de conta, código de autorização e ID token ----
const googleCodes = new Map(); // code -> { email, sub, nonce }
const googleLinked = new Set(); // e-mails com senha já vinculados ao Google
const fakeJwt = (payload) =>
  `e30.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.assinatura-de-mentira`;
const jwtPayload = (token) =>
  JSON.parse(Buffer.from(String(token).split(".")[1] ?? "", "base64url").toString());

/** GET /google/authorize: a tela de escolha de conta, como a do Google. */
function googleChooser(res, url) {
  const back = new URL(url.searchParams.get("redirect_uri"));
  const state = url.searchParams.get("state");
  const nonce = url.searchParams.get("nonce");
  const link = (email) => {
    const code = `code-${crypto.randomUUID()}`;
    googleCodes.set(code, { email, sub: `google-${email}`, nonce });
    const to = new URL(back);
    to.search = new URLSearchParams({ code, state }).toString();
    return `<li><a href="${to}">${email}</a></li>`;
  };
  const cancel = new URL(back);
  cancel.search = new URLSearchParams({ error: "access_denied", state }).toString();
  const fresh = `nova-${crypto.randomUUID().slice(0, 8)}@gmail.com`;
  res.writeHead(200, { "Content-Type": "text/html; charset=utf-8" });
  res.end(
    `<!doctype html><title>Escolha uma conta</title><h1>Escolha uma conta</h1><ul>${[fresh, "ana@exemplo.com", ...accounts.keys()].map(link).join("")}</ul><a href="${cancel}">Cancelar</a>`,
  );
}

/** 202 do login de operador: o tíquete do segundo passo, nunca tokens. */
function mfaChallenge(email) {
  seq++;
  const ticket = `tiquete-${seq}`;
  const status = email.startsWith("admin@novo.") ? "mfa_setup_required" : "mfa_required";
  mfaTickets.set(ticket, { email, status, used: false });
  return { status, mfa_ticket: ticket, expires_in: status === "mfa_required" ? 300 : 900 };
}

const needsMfa = (email) => (email ?? "").startsWith("admin@");

function tokens(email) {
  seq++;
  const access = `access-${seq}`;
  const refresh = `refresh-${seq}`;
  accessTokens.set(access, email);
  sessions.set(refresh, { email, used: false });
  // "curto@..." recebe um access token que vence logo: exercita o refresh do BFF
  const expires = email.startsWith("curto@") ? 61 : 900;
  return {
    access_token: access,
    token_type: "Bearer",
    expires_in: expires,
    refresh_token: refresh,
    refresh_expires_in: 2592000,
  };
}

function send(res, status, body, type = "application/json") {
  res.writeHead(status, { "Content-Type": type });
  res.end(body === undefined ? undefined : JSON.stringify(body));
}

const problem = (res, status, title) =>
  send(res, status, { type: "about:blank", title, status }, "application/problem+json");

// código do segundo fator errado: 422 no campo, como o identity
const validationProblem = (res, field) =>
  send(
    res,
    422,
    {
      type: "about:blank",
      title: "Dados inválidos",
      status: 422,
      errors: [{ field, message: "código inválido" }],
    },
    "application/problem+json",
  );

// ---------- fornecedor manual (#412) ----------
// O modo manual vale só para o operador de @manual.exemplo.com: os outros testes do painel rodam em paralelo no modo
// com API. O teste começa com POST /__manual/reset.
const adminEmail = (req) => accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", "")) ?? "";
const isManualAdmin = (req) => isAdmin(req) && adminEmail(req).endsWith("@manual.exemplo.com");
const MANUAL_TO_PLACE = "7c41e0aa-2b8f-4d1e-9a55-6f0b2d3c4e10";
const MANUAL_PLACED = "8d52f1bb-3c9a-4e2f-8b66-7a1c3e4d5f21";
const MANUAL_SHIPPED = "9e63a2cc-4dab-4f3a-9c77-8b2d4f5e6a32";
let manual;
function resetManual() {
  const base = {
    items: [{ sku: "FON-BT-01", name: "Fone de ouvido Bluetooth", quantity: 1 }],
    revenue_cents: 17980,
    freight_charged_cents: 1990,
    shipping_service: "economico",
    expected_product_cost_cents: 8990,
    expected_freight_cost_cents: 1990,
    customer_email: "ana@exemplo.com",
    ship_to: {
      name: "Ana Souza",
      cpf: "52998224725",
      phone: "11987654321",
      zip_code: "01310100",
      street: "Avenida Paulista",
      number: "1000",
      complement: "Apto 12",
      district: "Bela Vista",
      city: "São Paulo",
      state: "SP",
    },
    paid_product_cost_cents: null,
    paid_freight_cost_cents: null,
    tracking: null,
    placed_at: null,
    shipped_at: null,
    promised_by: "2026-12-28",
  };
  manual = new Map([
    [
      MANUAL_TO_PLACE,
      {
        ...base,
        order_id: MANUAL_TO_PLACE,
        status: "awaiting_staff",
        supplier_order_id: null,
        awaiting_since: iso(Date.now() - 26 * 3_600_000),
      },
    ],
    [
      MANUAL_PLACED,
      {
        ...base,
        order_id: MANUAL_PLACED,
        status: "placed",
        supplier_order_id: "PED-5501",
        awaiting_since: iso(Date.now() - 50 * 3_600_000),
        placed_at: iso(Date.now() - 40 * 3_600_000),
      },
    ],
    [
      MANUAL_SHIPPED,
      {
        ...base,
        order_id: MANUAL_SHIPPED,
        status: "shipped",
        supplier_order_id: "PED-5490",
        awaiting_since: iso(Date.now() - 90 * 3_600_000),
        placed_at: iso(Date.now() - 80 * 3_600_000),
        tracking: { number: "BR987654321BR", provider: "Correios", url: null },
        shipped_at: iso(Date.now() - 48 * 3_600_000),
      },
    ],
  ]);
}
resetManual();

/** As rotas do fornecedor manual: respondem a requisição que é delas e deixam as outras sem resposta. */
async function manualSupplier(req, res, url) {
  if (req.method === "POST" && url.pathname === "/__manual/reset") {
    resetManual();
    return res.writeHead(204).end();
  }
  if (url.pathname === "/fulfillment/v1/admin/supplier-catalog/sheet") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    if (req.method === "GET") {
      return isManualAdmin(req)
        ? send(res, 200, { enabled: true, products: 12, imported_at: iso(Date.now() - 5 * 86_400_000) })
        : send(res, 200, { enabled: false, products: 0, imported_at: null });
    }
    const body = await new Promise((resolve) => {
      const chunks = [];
      req.on("data", (c) => chunks.push(c));
      req.on("end", () => resolve(Buffer.concat(chunks).toString("utf8")));
    });
    if (body.includes("doze")) {
      res.writeHead(422, { "Content-Type": "application/problem+json" });
      return res.end(
        JSON.stringify({
          type: "about:blank",
          title: "Planilha com erro",
          status: 422,
          detail: "1 linha precisa de correção; nada mudou na loja.",
          sheet_errors: [{ line: 3, message: 'custo inválido: "doze" (ex.: 12,90)' }],
        }),
      );
    }
    return send(res, 200, { products: 2, sync: { created: 1, updated: 1, removed: 0, unchanged: 0 } });
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-catalog/sheet/template") {
    res.writeHead(200, { "Content-Type": "text/csv; charset=utf-8" });
    return res.end("sku;nome;categoria;custo;estoque;peso_g;foto\r\n");
  }
  const list = (status) => send(res, 200, { data: [...manual.values()].filter((o) => o.status === status) });
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-orders/to-place") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return list("awaiting_staff");
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-orders/manual") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return list(url.searchParams.get("status"));
  }
  // a correção do pedido feito (#480): o número no fornecedor e o que a equipe pagou, só feito ou em transporte
  const correction = url.pathname.match(
    /^\/fulfillment\/v1\/admin\/supplier-orders\/([0-9a-f-]{36})\/placement$/,
  );
  if (req.method === "PUT" && correction && manual.has(correction[1])) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    const order = manual.get(correction[1]);
    if (!["placed", "shipped"].includes(order.status)) return problem(res, 409, "Ação indisponível");
    const body = await json(req);
    Object.assign(order, {
      supplier_order_id: body.supplier_order_id,
      paid_product_cost_cents: body.paid_product_cost_cents ?? null,
      paid_freight_cost_cents: body.paid_freight_cost_cents ?? null,
    });
    return res.writeHead(204).end();
  }
  const action = url.pathname.match(
    /^\/fulfillment\/v1\/admin\/supplier-orders\/([0-9a-f-]{36})\/(placed|out-of-stock|tracking|delivered|cancel)$/,
  );
  if (req.method !== "POST" || !action || !manual.has(action[1])) return undefined;
  if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
  const order = manual.get(action[1]);
  const body = req.headers["content-type"]?.includes("json") ? await json(req) : {};
  const now = new Date().toISOString();
  switch (action[2]) {
    case "placed":
      if (order.status !== "awaiting_staff") return problem(res, 409, "Ação indisponível");
      Object.assign(order, {
        status: "placed",
        supplier_order_id: body.supplier_order_id,
        paid_product_cost_cents: body.paid_product_cost_cents ?? null,
        paid_freight_cost_cents: body.paid_freight_cost_cents ?? null,
        placed_at: now,
      });
      break;
    case "tracking":
      if (order.status !== "placed") return problem(res, 409, "Ação indisponível");
      Object.assign(order, {
        status: "shipped",
        tracking: { number: body.number, provider: body.carrier ?? null, url: body.url ?? null },
        shipped_at: now,
      });
      break;
    case "delivered":
      if (order.status !== "shipped") return problem(res, 409, "Ação indisponível");
      order.status = "delivered";
      break;
    case "out-of-stock":
      if (order.status !== "awaiting_staff") return problem(res, 409, "Ação indisponível");
      order.status = "failed";
      break;
    case "cancel":
      if (order.status === "placed" && !body?.cancelled_at_supplier)
        return problem(res, 409, "Cancele no fornecedor primeiro");
      order.status = "failed";
      break;
  }
  return res.writeHead(204).end();
}

async function json(req) {
  let raw = "";
  for await (const chunk of req) raw += chunk;
  return raw ? JSON.parse(raw) : {};
}

createServer(async (req, res) => {
  const url = new URL(req.url, `http://localhost:${port}`);
  if (req.method === "GET" && url.pathname === "/google/authorize") return googleChooser(res, url);
  if (req.method === "POST" && url.pathname === "/google/token") {
    let raw = "";
    for await (const chunk of req) raw += chunk;
    const form = new URLSearchParams(raw);
    const grant = googleCodes.get(form.get("code"));
    if (!grant || !form.get("code_verifier") || form.get("client_secret") !== "segredo-de-teste") {
      return send(res, 400, { error: "invalid_grant" });
    }
    googleCodes.delete(form.get("code"));
    return send(res, 200, { id_token: fakeJwt({ ...grant, email_verified: true }), token_type: "Bearer" });
  }
  const dlt = url.pathname.match(
    /^\/(orders|inventory|payments|notifications|fulfillment|catalog)\/v1\/admin\/dead-letters(\/replay)?$/,
  );
  if (dlt) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    // o payments está com o Kafka fora: a tela explica em vez de quebrar
    if (dlt[1] === "payments") return problem(res, 503, "Serviço indisponível");
    const items = deadLetterQueues[dlt[1]];
    // reprocessadas, falham de novo e voltam para a fila (são mensagens envenenadas)
    if (dlt[2]) return send(res, 200, { replayed: items.length });
    return send(res, 200, { items });
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier/status") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return send(res, 200, {
      name: "cj",
      host: "supplier-sim",
      reachable: true,
      latency_ms: 12,
      error: null,
      own_credentials: false,
      webhook_secret_configured: true,
      products_in_feed: 40,
      last_stock_read_at: "2026-10-30T12:00:00Z",
      last_webhook_at: "2026-10-30T12:05:00Z",
      ship_within_hours: 72,
      deliver_within_hours: 360,
      return_within_hours: 360,
    });
  }
  // o resumo de vendas da visão geral (#382): um dia com venda e estorno, e os outros zerados
  if (req.method === "GET" && url.pathname === "/orders/v1/admin/sales-summary") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    if (!from || !to) return problem(res, 422, "Dados inválidos");
    const brl = (cents) => ({ amount_cents: cents, currency: "BRL" });
    const days = Math.round((Date.parse(to) - Date.parse(from)) / 86400000);
    const daily = Array.from({ length: days }, (_, i) => {
      const date = new Date(Date.parse(from) + i * 86400000 - 3 * 3600000).toISOString().slice(0, 10);
      const last = i === days - 1;
      return {
        date,
        paid_orders: last ? 3 : 0,
        gross_sales: brl(last ? 36900 : 0),
        refunds: brl(last ? 9900 : 0),
      };
    });
    return send(res, 200, {
      from,
      to,
      orders: { placed: 4, paid: 3, refunded: 1, not_paid: 1 },
      gross_sales: brl(36900),
      refunds: brl(9900),
      net_sales: brl(27000),
      average_ticket: brl(12300),
      shipping_charged: brl(4500),
      daily,
      by_payment_method: [
        { method: "card", paid_orders: 2, gross_sales: brl(31900) },
        { method: "pix", paid_orders: 1, gross_sales: brl(5000) },
      ],
      top_products: [
        {
          product_id: "0199f6a0-0000-7000-8000-000000000001",
          name: "Fone Sonix",
          quantity: 2,
          revenue: brl(20000),
        },
        {
          product_id: "0199f6a0-0000-7000-8000-000000000002",
          name: "Mouse sem Fio",
          quantity: 1,
          revenue: brl(5500),
        },
      ],
    });
  }
  // os pedidos do painel (#382): o contato de quem comprou, sem CPF nem endereço
  if (req.method === "GET" && url.pathname === "/orders/v1/admin/orders") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    const q = (url.searchParams.get("q") ?? "").toLowerCase();
    const status = url.searchParams.get("status");
    const data = adminOrders
      .filter((o) => !status || o.status === status)
      .filter(
        (o) =>
          !q ||
          o.customer.email.includes(q) ||
          (o.customer.name ?? "").toLowerCase().includes(q) ||
          o.id.replace(/-/g, "").endsWith(q),
      )
      // a lista leva só o resumo: o que é do detalhe sai
      .map((o) => {
        const summary = { ...o };
        for (const k of ["items", "items_total", "shipping", "tracking", "invoice_number", "updated_at"])
          delete summary[k];
        return summary;
      });
    return send(res, 200, { data, page: { next_cursor: null, has_more: false } });
  }
  // o pagamento do pedido e o estorno pelo painel (#396)
  if (req.method === "GET" && url.pathname === "/payments/v1/payments") {
    const order = adminOrders.find((o) => o.id === url.searchParams.get("order_id"));
    const data = order
      ? [
          {
            id: "01a18d66-0000-7000-8000-0000c4000004",
            order_id: order.id,
            status: "approved",
            method: order.payment.method,
          },
        ]
      : [];
    return send(res, 200, { data });
  }
  const refundAgain = url.pathname.match(/^\/payments\/v1\/admin\/payments\/([0-9a-f-]{36})\/refunds$/);
  if (req.method === "POST" && refundAgain) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    refundsAgain.add(req.headers["idempotency-key"]);
    return send(res, 200, { id: refundAgain[1], status: "refunded" });
  }
  if (req.method === "GET" && url.pathname.startsWith("/orders/v1/admin/orders/")) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    const order = adminOrders.find((o) => o.id === url.pathname.split("/").pop());
    return order ? send(res, 200, order) : problem(res, 404, "Pedido não encontrado");
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/order-margins") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    const row = (id, outcome, revenue, cost, freight, fee, margin, created) => ({
      order_id: id,
      outcome,
      revenue_cents: revenue,
      product_cost_cents: cost,
      freight_cents: freight,
      fee_cents: fee,
      margin_cents: margin,
      customer_email: "bia@exemplo.com",
      created_at: created,
    });
    const all = [
      row(
        "01a18d66-0000-7000-8000-0000a1000000",
        "delivered",
        12000,
        6000,
        2000,
        598,
        3402,
        "2026-10-20T15:00:00Z",
      ),
      row(
        "01a18d66-0000-7000-8000-0000a2000000",
        "in_progress",
        5000,
        4800,
        1000,
        249,
        -1049,
        "2026-10-20T16:00:00Z",
      ),
      row(
        "01a18d66-0000-7000-8000-0000a3000000",
        "delivered",
        9000,
        null,
        1000,
        90,
        null,
        "2026-10-25T15:00:00Z",
      ),
      row(
        "01a18d66-0000-7000-8000-0000a4000000",
        "returned",
        7000,
        3000,
        1000,
        70,
        null,
        "2026-11-01T15:00:00Z",
      ),
    ];
    // o período (#282): criados em [from, to), e a soma como a do fulfillment
    const from = url.searchParams.get("from");
    const to = url.searchParams.get("to");
    const data = all.filter(
      (o) =>
        (!from || o.created_at >= new Date(from).toISOString()) &&
        (!to || o.created_at < new Date(to).toISOString()),
    );
    const counted = data.filter((o) => o.margin_cents != null);
    const sum = (key) => counted.reduce((n, o) => n + o[key], 0);
    const refunded = data.filter((o) => o.outcome === "cancelled" || o.outcome === "returned").length;
    return send(res, 200, {
      data,
      totals: {
        orders: data.length,
        counted: counted.length,
        refunded,
        incomplete: data.length - counted.length - refunded,
        revenue_cents: sum("revenue_cents"),
        product_cost_cents: sum("product_cost_cents"),
        freight_cents: sum("freight_cents"),
        fee_cents: sum("fee_cents"),
        margin_cents: sum("margin_cents"),
      },
    });
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-orders/failed") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return send(res, 200, {
      data: [
        {
          order_id: "01a18d66-0000-7000-8000-00003f6a9c10",
          reason: "out_of_stock",
          detail: "Out of stock: SIM-V-003",
          supplier: "cj",
          supplier_order_id: null,
          customer_email: "edu@exemplo.com",
          attempts: 1,
          created_at: iso(Date.now() - 86_400_000),
          failed_at: iso(Date.now() - 86_000_000),
        },
      ],
    });
  }
  // as rotas do fornecedor manual respondem elas mesmas; as outras seguem adiante
  await manualSupplier(req, res, url);
  if (res.headersSent) return;
  const orderAction = url.pathname.match(
    /^\/fulfillment\/v1\/admin\/supplier-orders\/([0-9a-f-]{36})\/(retry|cancel)$/,
  );
  if (req.method === "POST" && orderAction) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    // o pedido 9d2c7a40 já foi despachado: o cancelamento dá 409
    if (orderAction[1].endsWith("9d2c7a40")) return problem(res, 409, "Ação indisponível");
    return res.writeHead(204).end();
  }
  const instruction = url.pathname.match(
    /^\/fulfillment\/v1\/admin\/supplier-returns\/([0-9a-f-]{36})\/customer-instruction$/,
  );
  if (req.method === "POST" && instruction) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    if (instruction[1] !== REFUSED_AT_OPENING) return problem(res, 409, "Ação indisponível");
    const body = await json(req);
    const previous = returnInstructions.get(instruction[1]);
    if (previous && previous.instruction !== body.instruction) return problem(res, 409, "Ação indisponível");
    if (!previous) returnInstructions.set(instruction[1], { ...body, at: new Date().toISOString() });
    return res.writeHead(204).end();
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-returns") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return send(res, 200, { data: supplierReturns() });
  }
  if (req.method === "GET" && url.pathname === "/fulfillment/v1/admin/supplier-orders/overdue") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return send(res, 200, { data: overdueOrders() });
  }
  if (url.pathname.startsWith("/catalog/v1/admin/pricing-rules")) {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    if (req.method === "GET") {
      return send(res, 200, { ...pricing, exchange_rates: { USD: "5.40" } });
    }
    const category = url.pathname.match(/\/categories\/([0-9a-f-]{36})$/)?.[1];
    if (req.method === "DELETE") {
      const before = pricing.rules.length;
      pricing.rules = pricing.rules.filter((r) => r.category_id !== category);
      return before === pricing.rules.length
        ? problem(res, 404, "Recurso não encontrado")
        : send(res, 200, { repriced: 1 });
    }
    const body = await json(req);
    const rule = { ...body, updated_at: new Date().toISOString() };
    if (category) {
      pricing.rules = [
        ...pricing.rules.filter((r) => r.category_id !== category),
        { category_id: category, ...rule },
      ];
    } else {
      pricing.default = rule;
    }
    return send(res, 200, { rule: category ? { category_id: category, ...rule } : rule, repriced: 1 });
  }
  if (req.method === "POST" && url.pathname === "/fulfillment/v1/admin/supplier-catalog/sync") {
    if (!isAdmin(req)) return problem(res, 401, "Não autenticado");
    return send(res, 200, { created: 2, updated: 1, removed: 0, unchanged: 27 });
  }
  if (url.pathname.startsWith("/catalog/v1/admin/") || url.pathname.startsWith("/inventory/v1/admin/")) {
    return admin(req, res, url);
  }
  if (url.pathname === "/sim/card-token") return tokenize(req, res);
  if (req.method === "POST" && url.pathname === "/fulfillment/v1/shipping/quote") {
    const { zip_code: zip, items } = await json(req);
    const digits = String(zip ?? "").replace(/\D/g, "");
    if (digits.length !== 8 || !items?.length) return problem(res, 422, "Dados inválidos");
    if (digits === "01310200") await new Promise((r) => setTimeout(r, 1500));
    const ids = items.map((i) => i.product_id);
    return send(res, 200, {
      zip_code: digits,
      options: shippingFor(digits, ids.every(Boolean) ? ids : []),
    });
  }
  // o DANFE da nota (#221): só a do pedido confirmado da Ana
  const danfe = url.pathname.match(/^\/fulfillment\/v1\/me\/invoices\/([0-9a-f-]{36})\/danfe$/);
  if (req.method === "GET" && danfe) {
    const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
    if (!email) return problem(res, 401, "Não autenticado");
    const found = email === "ana@exemplo.com" && orders.find((o) => o.id === danfe[1] && o.invoice);
    if (!found) return problem(res, 404, "Recurso não encontrado");
    res.writeHead(200, {
      "Content-Type": "application/pdf",
      "Content-Disposition": `attachment; filename="nfe-${found.invoice.number}.pdf"`,
    });
    return res.end("%PDF-1.4\n%%EOF\n");
  }
  // o ViaCEP: um CEP da Paulista, um de cidade pequena (sem rua) e o resto não existe
  const cep = url.pathname.match(/^\/cep\/(\d{8})\/json\/$/);
  if (req.method === "GET" && cep) {
    const known = {
      "01310100": { logradouro: "Avenida Paulista", bairro: "Bela Vista", localidade: "São Paulo", uf: "SP" },
      "01310200": { logradouro: "Avenida Paulista", bairro: "Bela Vista", localidade: "São Paulo", uf: "SP" },
      37200000: { logradouro: "", bairro: "", localidade: "Lavras", uf: "MG" },
    }[cep[1]];
    return send(res, 200, known ?? { erro: "true" });
  }
  // o banco do cliente pagando o PIX
  const pixPay = url.pathname.match(/^\/__pix\/([0-9a-f-]{36})\/pay$/);
  if (req.method === "POST" && pixPay) {
    const entry = placed.get(pixPay[1]);
    if (!entry || entry.order.payment.method !== "pix") return problem(res, 404, "Recurso não encontrado");
    entry.paidAt = Date.now();
    return res.writeHead(204).end();
  }
  // a desistência (#223): o entregue vira devolvido, e o stub já o dá por estornado
  const ret = url.pathname.match(/^\/orders\/v1\/orders\/([0-9a-f-]{36})\/return$/);
  if (req.method === "POST" && ret) {
    const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
    if (!email) return problem(res, 401, "Não autenticado");
    const found = email === "ana@exemplo.com" && orders.find((o) => o.id === ret[1]);
    if (!found) return problem(res, 404, "Recurso não encontrado");
    if (found.status === "delivered") {
      const now = new Date().toISOString();
      Object.assign(found, {
        status: "returned",
        return_requested_at: now,
        returnable_until: null,
        refunded_at: now,
        updated_at: now,
        return_invoice: { access_key: "4".repeat(44), number: 2001, series: 1, issued_at: now },
      });
    } else if (found.status !== "returned") {
      return problem(res, 409, "O pedido não pode ser devolvido");
    }
    return send(res, 200, found);
  }
  const order = url.pathname.match(/^\/orders\/v1\/orders\/([0-9a-f-]{36})(\/events)?$/);
  if (req.method === "GET" && order) {
    const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
    if (!email) return problem(res, 401, "Não autenticado");
    const entry = placed.get(order[1]);
    if (entry && entry.owner === email)
      return order[2] ? streamOrder(req, res, entry) : send(res, 200, orderAt(entry));
    const found = !order[2] && email === "ana@exemplo.com" && orders.find((o) => o.id === order[1]);
    return found ? send(res, 200, found) : problem(res, 404, "Recurso não encontrado");
  }
  // o teste muda o preço de um produto no meio da compra: POST /__price {product_id, amount_cents}
  if (req.method === "POST" && url.pathname === "/__price") {
    const { product_id, amount_cents } = await json(req);
    const product = products.find((p) => p.id === product_id);
    product.price = { amount_cents, currency: "BRL" };
    product.effective_price = { amount_cents, currency: "BRL" };
    return send(res, 200, product);
  }
  // a API fora para um produto só (a reserva da vitrine): os outros testes, em paralelo, não sentem
  const down = url.pathname.match(/^\/__teste\/fora\/([0-9a-f-]{36})$/);
  if (req.method === "POST" && down) {
    productsDown.add(down[1]);
    return res.writeHead(204).end();
  }
  const detail = url.pathname.match(/^\/catalog\/v1\/products\/([0-9a-f-]{36})$/);
  if (req.method === "GET" && detail) {
    if (productsDown.has(detail[1])) return problem(res, 503, "Serviço indisponível");
    const product = products.find((p) => p.id === detail[1]);
    return product ? send(res, 200, product) : problem(res, 404, "Recurso não encontrado");
  }
  switch (`${req.method} ${url.pathname}`) {
    case "GET /catalog/v1/products":
      // a busca "limite" faz o papel do rate limit do gateway
      if (url.searchParams.get("q") === "limite") {
        res.setHeader("Retry-After", "42");
        return problem(res, 429, "Muitas requisições");
      }
      // a busca "fora" faz o papel da API fora do ar, como na troca dos contêineres de um deploy (#370)
      if (url.searchParams.get("q") === "fora") return problem(res, 503, "Serviço indisponível");
      return send(res, 200, listProducts(url));
    case "GET /catalog/v1/brands":
      return send(res, 200, listBrands(url));
    case "GET /catalog/v1/category-counts":
      return send(res, 200, listCategoryCounts(url));
    case "GET /inventory/v1/stock": {
      const ids = (url.searchParams.get("product_ids") ?? "").split(",").filter(Boolean);
      return send(res, 200, {
        data: ids.map((id) => {
          const available = stock(id);
          return {
            product_id: id,
            on_hand: available,
            reserved: 0,
            available,
            updated_at: "2026-10-01T10:00:00Z",
          };
        }),
      });
    }
    case "GET /catalog/v1/categories":
      // como o catalog: os produtos à venda na categoria e nas descendentes
      return send(res, 200, {
        data: categories.map((c) => ({
          ...c,
          active_products: products.filter(
            (p) => p.status === "active" && subtree(c.id).includes(p.category.slug),
          ).length,
        })),
      });
    case "GET /catalog/v1/campaigns":
      return send(res, 200, { data: campaigns });
    case "POST /identity/v1/accounts": {
      const { email, password, name } = await json(req);
      if (accounts.has(email) || email === "ana@exemplo.com") return problem(res, 409, "Conflito");
      accounts.set(email, { password, name });
      return send(res, 201, {
        id: crypto.randomUUID(),
        email,
        name,
        email_confirmed: false,
        roles: ["customer"],
        created_at: new Date().toISOString(),
      });
    }
    case "POST /identity/v1/sessions/guest": {
      const { email, name, checkout_key: key } = await json(req);
      if (!/@.+\./.test(email ?? "")) return problem(res, 422, "Dados inválidos");
      const same = guestKeys.get(key);
      if (same && same !== email) return problem(res, 409, "Compra já iniciada");
      guestKeys.set(key, email);
      guests.set(email, name);
      return send(res, 200, tokens(email));
    }
    case "POST /identity/v1/sessions/guest/access": {
      const { token } = await json(req);
      if (token !== "link-bom") return problem(res, 401, "Não autenticado");
      guests.set("convidada@exemplo.com", "Bia Lima");
      return send(res, 200, tokens("convidada@exemplo.com"));
    }
    case "POST /identity/v1/sessions/guest/access/resend":
      return send(res, 202, {});
    case "POST /identity/v1/accounts/confirm-email/resend":
      res.writeHead(202).end();
      return;
    case "POST /identity/v1/accounts/password-reset":
      res.writeHead(202).end();
      return;
    case "POST /identity/v1/accounts/password-reset/confirm": {
      // "token-bom..." vale uma vez só, como o do identity; a senha nova passa a valer no login de quem pediu
      const { token, password } = await json(req);
      if (!token?.startsWith("token-bom") || usedResetTokens.has(token)) {
        return send(
          res,
          422,
          {
            type: "about:blank",
            title: "Dados inválidos",
            status: 422,
            errors: [{ field: "token", message: "link de redefinição inválido ou vencido" }],
          },
          "application/problem+json",
        );
      }
      usedResetTokens.add(token);
      accounts.set("redefinir@exemplo.com", { password, name: "Rita Dias" });
      res.writeHead(204).end();
      return;
    }
    case "POST /identity/v1/accounts/confirm-email": {
      const { token } = await json(req);
      if (token !== "token-bom") return problem(res, 422, "Dados inválidos");
      res.writeHead(204).end();
      return;
    }
    case "POST /orders/v1/orders": {
      const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
      if (!email) return problem(res, 401, "Não autenticado");
      return placeOrder(req, res, email);
    }
    case "GET /orders/v1/me/orders": {
      const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
      if (!email) return problem(res, 401, "Não autenticado");
      return send(res, 200, {
        data: email === "ana@exemplo.com" ? orders : [],
        page: { next_cursor: null, has_more: false },
      });
    }
    case "POST /identity/v1/sessions": {
      const { email, password } = await json(req);
      const account = accounts.get(email);
      if (account ? account.password === password : password === "senha-boa-123") {
        // "confirmar@..." tem a confirmação de e-mail exigida: senha certa, mas 403
        if (email.startsWith("confirmar@")) return problem(res, 403, "E-mail não confirmado");
        return needsMfa(email) ? send(res, 202, mfaChallenge(email)) : send(res, 200, tokens(email));
      }
      return problem(res, 401, "Não autenticado");
    }
    case "POST /identity/v1/sessions/google": {
      const { id_token, nonce } = await json(req);
      const claims = jwtPayload(id_token);
      if (claims.nonce !== nonce) return problem(res, 401, "Não autenticado");
      // e-mail com conta e senha (a Ana, as criadas no cadastro): o Google só entra depois do vínculo
      const hasPassword = claims.email === "ana@exemplo.com" || accounts.has(claims.email);
      if (hasPassword && !googleLinked.has(claims.email)) return problem(res, 409, "Conta já existe");
      // o Google é só o primeiro fator da conta da equipe
      if (needsMfa(claims.email)) return send(res, 202, mfaChallenge(claims.email));
      return send(res, 200, tokens(claims.email));
    }
    case "POST /identity/v1/sessions/google/link": {
      const { id_token, nonce, password } = await json(req);
      const claims = jwtPayload(id_token);
      const expected = accounts.get(claims.email)?.password ?? "senha-boa-123";
      if (claims.nonce !== nonce || password !== expected) return problem(res, 401, "Não autenticado");
      googleLinked.add(claims.email);
      if (needsMfa(claims.email)) return send(res, 202, mfaChallenge(claims.email));
      return send(res, 200, tokens(claims.email));
    }
    case "POST /identity/v1/sessions/mfa": {
      const { ticket, code, recovery_code: recovery } = await json(req);
      const pending = mfaTickets.get(ticket);
      if (!pending || pending.used || pending.status !== "mfa_required")
        return problem(res, 401, "Não autenticado");
      const valid = recovery
        ? RECOVERY_CODES.includes(recovery) && !usedRecoveryCodes.has(`${pending.email}/${recovery}`)
        : code === ADMIN_TOTP;
      if (!valid) return validationProblem(res, recovery ? "RecoveryCode" : "Code");
      if (recovery) usedRecoveryCodes.add(`${pending.email}/${recovery}`);
      pending.used = true;
      return send(res, 200, tokens(pending.email));
    }
    case "POST /identity/v1/sessions/mfa/setup": {
      const pending = mfaTickets.get((await json(req)).ticket);
      if (!pending || pending.used || pending.status !== "mfa_setup_required")
        return problem(res, 401, "Não autenticado");
      return send(res, 200, {
        shared_key: "jbsw y3dp ehpk 3pxp",
        authenticator_uri: `otpauth://totp/Embrulhado:${encodeURIComponent(pending.email)}?secret=JBSWY3DPEHPK3PXP&issuer=Embrulhado`,
      });
    }
    case "POST /identity/v1/sessions/mfa/setup/confirm": {
      const { ticket, code } = await json(req);
      const pending = mfaTickets.get(ticket);
      if (!pending || pending.used || pending.status !== "mfa_setup_required")
        return problem(res, 401, "Não autenticado");
      if (code !== ADMIN_TOTP) return validationProblem(res, "Code");
      pending.used = true;
      return send(res, 200, { ...tokens(pending.email), recovery_codes: RECOVERY_CODES });
    }
    case "POST /identity/v1/me/two-factor/recovery-codes": {
      if (!isAdmin(req)) return problem(res, 403, "Sem permissão");
      if ((await json(req)).code !== ADMIN_TOTP) return validationProblem(res, "Code");
      return send(res, 200, { recovery_codes: RECOVERY_CODES.map((c) => c.replace("rcod", "novo")) });
    }
    case "POST /identity/v1/sessions/refresh": {
      stats.refresh++;
      const session = sessions.get((await json(req)).refresh_token);
      if (!session || session.used) return problem(res, 401, "Não autenticado");
      session.used = true;
      return send(res, 200, tokens(session.email));
    }
    case "POST /identity/v1/sessions/revoke":
      stats.revoke++;
      sessions.delete((await json(req)).refresh_token);
      res.writeHead(204).end();
      return;
    case "GET /identity/v1/me": {
      const email = accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", ""));
      if (!email) return problem(res, 401, "Não autenticado");
      return send(res, 200, {
        id: guests.has(email)
          ? (guestIds.get(email) ?? guestIds.set(email, crypto.randomUUID()).get(email))
          : "0199f6a0-0000-7000-8000-000000000a1a",
        email,
        name: guests.get(email) ?? "Ana Souza",
        email_confirmed: true,
        is_guest: guests.has(email),
        // "admin@..." é da equipe: abre o painel
        roles: email.startsWith("admin@") ? ["admin"] : ["customer"],
        created_at: "2026-10-01T10:00:00Z",
        two_factor_enabled: needsMfa(email),
        recovery_codes_left: needsMfa(email)
          ? 10 - [...usedRecoveryCodes].filter((c) => c.startsWith(`${email}/`)).length
          : null,
      });
    }
    case "GET /__stats":
      return send(res, 200, stats);
    default:
      return problem(res, 404, "Recurso não encontrado");
  }
}).listen(port, () => console.log(`stub da API em http://localhost:${port}`));
