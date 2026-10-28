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

// pedidos da Ana: um confirmado e um cancelado por falta de estoque
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
  total: { amount_cents: unit * qty, currency: "BRL" },
  payment_id: null,
  created_at: "2026-10-20T15:00:00Z",
  updated_at: "2026-10-20T15:01:00Z",
});
const orders = [
  orderOf("4f2a9c1b-0000-7000-8000-000000000001", "confirmed", null, 1, 263992),
  orderOf("7c3e5d2a-0000-7000-8000-000000000002", "cancelled", "out_of_stock", 2, 263992),
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
    created_at: "2026-10-01T10:00:00Z",
    updated_at: "2026-10-01T10:00:00Z",
  };
});

// o notebook (2) tem só 3 unidades e o último fone (32) esgotou: exercitam os limites do carrinho
const baseStock = (id) => ({ [products[2].id]: 3, [products[32].id]: 0 })[id] ?? 100;
// ajustes feitos pelo painel, por produto
const adjustments = new Map();
const stock = (id) => baseStock(id) + (adjustments.get(id) ?? 0);

// o painel: situação e versão de cada produto (a versão é o ETag)
for (const p of products) Object.assign(p, { status: "active", version: 1 });
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
}; // campanhas criadas pelo painel nos testes (não mudam o preço da vitrine)
const isAdmin = (req) =>
  (accessTokens.get((req.headers.authorization ?? "").replace("Bearer ", "")) ?? "").startsWith("admin@");

/** Corpo do ProductInput aplicado ao produto (o preço novo vale para o efetivo sem campanha). */
function applyInput(p, body) {
  const category = categories.find((c) => c.id === body.category_id) ?? p.category;
  Object.assign(p, {
    sku: body.sku,
    name: body.name,
    description: body.description ?? "",
    brand: body.brand ?? null,
    image_url: body.image_url ?? null,
    status: body.status,
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
    const p = {
      id: crypto.randomUUID(),
      campaign: null,
      image: null,
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

/** O pedido no instante `now`: reservando, cobrando e, no fim, confirmado ou cancelado. */
function orderAt(entry, now = Date.now()) {
  const { order, reserveAt, endAt, outcome } = entry;
  if (outcome === "out_of_stock" && now >= reserveAt) {
    return { ...order, status: "cancelled", cancellation_reason: "out_of_stock", updated_at: iso(reserveAt) };
  }
  const reserved = now >= reserveAt ? { stock_reserved_at: iso(reserveAt), updated_at: iso(reserveAt) } : {};
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
const eventId = (o) => String(Date.parse(o.updated_at));

async function placeOrder(req, res, email) {
  const key = req.headers["idempotency-key"];
  if (!key) return problem(res, 400, "Requisição malformada");
  const existing = byKey.get(`${email}:${key}`);
  const { items } = await json(req);
  if (existing) return send(res, 202, orderAt(placed.get(existing)));
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
  if (
    lines.some((l) => !l.product || l.expected_unit_price_cents !== l.product.effective_price.amount_cents)
  ) {
    return send(
      res,
      409,
      { type: "about:blank", title: "Itens recusados pelo catálogo", status: 409, trace_id: TRACE },
      "application/problem+json",
    );
  }
  const orderItems = lines.map(({ product, quantity }) => ({
    product_id: product.id,
    sku: product.sku,
    name: product.name,
    quantity,
    unit_price: product.effective_price,
    line_total: { amount_cents: product.effective_price.amount_cents * quantity, currency: "BRL" },
    campaign_id: product.campaign?.id ?? null,
  }));
  const total = orderItems.reduce((sum, i) => sum + i.line_total.amount_cents, 0);
  const now = Date.now();
  const id = crypto.randomUUID();
  const order = {
    id,
    customer_id: ANA,
    status: "pending",
    cancellation_reason: null,
    items: orderItems,
    total: { amount_cents: total, currency: "BRL" },
    payment_id: null,
    stock_reserved_at: null,
    created_at: iso(now),
    updated_at: iso(now),
  };
  // o gateway falso recusa acima de R$ 50.000,00; o inventory, acima do saldo
  const outcome = lines.some((l) => l.quantity > stock(l.product.id))
    ? "out_of_stock"
    : total > 5_000_000
      ? "payment_failed"
      : "confirmed";
  placed.set(id, { order, owner: email, reserveAt: now + 800, endAt: now + 1600, outcome });
  byKey.set(`${email}:${key}`, id);
  stats.ordersPlaced++;
  stats.ordersByKey[key] = (stats.ordersByKey[key] ?? 0) + 1;
  return send(res, 202, order);
}

/** GET .../events: o estado atual e cada mudança, em Server-Sent Events, até o estado final. */
function streamOrder(req, res, entry) {
  const last = req.headers["last-event-id"];
  let current = orderAt(entry);
  if (current.status !== "pending" && last === eventId(current)) return res.writeHead(204).end();
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
    if (current.status !== "pending") {
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
    /^\/(orders|inventory|payments|notifications)\/v1\/admin\/dead-letters(\/replay)?$/,
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
  if (url.pathname.startsWith("/catalog/v1/admin/") || url.pathname.startsWith("/inventory/v1/admin/")) {
    return admin(req, res, url);
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
  const detail = url.pathname.match(/^\/catalog\/v1\/products\/([0-9a-f-]{36})$/);
  if (req.method === "GET" && detail) {
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
      return send(res, 200, { data: categories });
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
    case "POST /identity/v1/accounts/confirm-email/resend":
      res.writeHead(202).end();
      return;
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
        return email.startsWith("confirmar@")
          ? problem(res, 403, "E-mail não confirmado")
          : send(res, 200, tokens(email));
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
      return send(res, 200, tokens(claims.email));
    }
    case "POST /identity/v1/sessions/google/link": {
      const { id_token, nonce, password } = await json(req);
      const claims = jwtPayload(id_token);
      const expected = accounts.get(claims.email)?.password ?? "senha-boa-123";
      if (claims.nonce !== nonce || password !== expected) return problem(res, 401, "Não autenticado");
      googleLinked.add(claims.email);
      return send(res, 200, tokens(claims.email));
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
        id: "0199f6a0-0000-7000-8000-000000000a1a",
        email,
        name: "Ana Souza",
        email_confirmed: true,
        // "admin@..." é da equipe: abre o painel
        roles: email.startsWith("admin@") ? ["admin"] : ["customer"],
        created_at: "2026-10-01T10:00:00Z",
      });
    }
    case "GET /__stats":
      return send(res, 200, stats);
    default:
      return problem(res, 404, "Recurso não encontrado");
  }
}).listen(port, () => console.log(`stub da API em http://localhost:${port}`));
