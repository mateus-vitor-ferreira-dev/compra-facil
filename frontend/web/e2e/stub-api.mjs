// API falsa para os testes de navegador e o Lighthouse: responde como o gateway (/identity, /catalog)
// o suficiente para o BFF. Guarda o estado em memória e expõe contadores em /__stats.
import { createServer } from "node:http";

const port = Number(process.env.STUB_PORT ?? 4010);
const sessions = new Map(); // refresh token -> { email, used }
const accessTokens = new Map(); // access token -> email
const stats = { refresh: 0, revoke: 0 };
let seq = 0;

const products = [
  ['Smart TV 55" QLED', 329990, 263992],
  ["Fone Bluetooth com cancelamento de ruído", 49990, 49990],
  ["Notebook Core i7 16GB SSD 512GB", 549990, 494991],
].map(([name, price, effective], i) => ({
  id: `0199f6a0-0000-7000-8000-00000000000${i}`,
  sku: `SKU-${i}`,
  name,
  description: "",
  category: { id: "0199f6a0-0000-7000-8000-0000000000aa", slug: "tv", name: "TVs" },
  image_url: null,
  image: null,
  price: { amount_cents: price, currency: "BRL" },
  effective_price: { amount_cents: effective, currency: "BRL" },
  campaign: null,
  created_at: "2026-10-01T10:00:00Z",
  updated_at: "2026-10-01T10:00:00Z",
}));

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
  switch (`${req.method} ${url.pathname}`) {
    case "GET /catalog/v1/products":
      return send(res, 200, { data: products, page: { next_cursor: null, has_more: false } });
    case "POST /identity/v1/sessions": {
      const { email, password } = await json(req);
      return password === "senha-boa-123"
        ? send(res, 200, tokens(email))
        : problem(res, 401, "Não autenticado");
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
