// API falsa para os testes de navegador e o Lighthouse: responde como o gateway (/identity, /catalog)
// o suficiente para o BFF. Guarda o estado em memória e expõe contadores em /__stats.
import { createServer } from "node:http";

const port = Number(process.env.STUB_PORT ?? 4010);
const sessions = new Map(); // refresh token -> { email, used }
const accessTokens = new Map(); // access token -> email
const stats = { refresh: 0, revoke: 0, ordersPlaced: 0 };
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
  ['Smart TV 55" QLED', "tv", 329990, 263992],
  ["Fone Bluetooth com cancelamento de ruído", "fones", 49990, 49990],
  ["Notebook Core i7 16GB SSD 512GB", "notebooks", 549990, 439992],
];
// 30 fones a mais: a categoria passa de uma página (24) e exercita o "Ver mais"
for (let i = 1; i <= 30; i++)
  seed.push([`Fone de ouvido modelo ${i}`, "fones", 9990 + i * 100, 9990 + i * 100]);

const products = seed.map(([name, slug, price, effective], i) => {
  const { id, name: categoryName } = bySlug[slug];
  const category = { id, slug, name: categoryName };
  return {
    id: `0199f6a0-0000-7000-8000-${String(i).padStart(12, "0")}`,
    sku: `SKU-${i}`,
    name,
    description: "",
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
const stock = (id) => ({ [products[2].id]: 3, [products[32].id]: 0 })[id] ?? 100;

/** GET /v1/products com os filtros que a vitrine usa; o cursor é o índice do próximo item. */
function listProducts(url) {
  const q = url.searchParams.get("q")?.toLowerCase();
  const slug = url.searchParams.get("category");
  const family = slug
    ? categories.filter((c) => c.slug === slug || c.parent_id === bySlug[slug]?.id).map((c) => c.slug)
    : null;
  const matches = products.filter(
    (p) =>
      (!q || p.name.toLowerCase().includes(q)) &&
      (!family || family.includes(p.category.slug)) &&
      (url.searchParams.get("on_sale") !== "true" || p.campaign),
  );
  const start = Number(url.searchParams.get("cursor") ?? 0);
  const limit = Number(url.searchParams.get("limit") ?? 20);
  const data = matches.slice(start, start + limit);
  const more = start + limit < matches.length;
  return { data, page: { next_cursor: more ? String(start + limit) : null, has_more: more } };
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
        roles: ["customer"],
        created_at: "2026-10-01T10:00:00Z",
      });
    }
    case "GET /__stats":
      return send(res, 200, stats);
    default:
      return problem(res, 404, "Recurso não encontrado");
  }
}).listen(port, () => console.log(`stub da API em http://localhost:${port}`));
