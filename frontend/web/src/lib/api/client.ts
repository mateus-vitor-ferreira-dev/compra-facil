import "server-only";
import { headers } from "next/headers";
import createClient, { type Middleware } from "openapi-fetch";
import { env } from "@/lib/env";
import { gatewayHeaders } from "./gateway-headers";
import type { paths as catalog } from "./schema/catalog";
import type { paths as identity } from "./schema/identity";
import type { paths as inventory } from "./schema/inventory";
import type { paths as orders } from "./schema/orders";
import type { paths as payments } from "./schema/payments";

/** Como chamar: com o token do cliente logado e/ou com cache de dados do Next (revalidate, tags). */
export type CallOptions = { token?: string; revalidate?: number | false; tags?: string[] };

/**
 * Clientes tipados das APIs (tipos gerados de contracts/openapi), sempre pelo gateway:
 * `${API_URL}/<serviço>/v1/...`. Com `revalidate`, a resposta fica no cache de dados do Next por esse
 * tempo (a mesma URL não volta à API); com `token`, vai o access token do cliente logado.
 */
function client<Paths extends object>(service: string, options: CallOptions = {}) {
  const { token, revalidate, tags } = options;
  const cached = revalidate !== undefined || tags !== undefined;
  const api = createClient<Paths>({
    baseUrl: `${env.API_URL.replace(/\/$/, "")}/${service}`,
    // os contratos pedem as listas na query separadas por vírgula (style form, explode false):
    // ?product_ids=a,b e não ?product_ids=a&product_ids=b, que o inventory leria como um id só
    querySerializer: { array: { style: "form", explode: false } },
    // o openapi-fetch chama fetch(request): o cache do Next vem no segundo argumento
    fetch: cached
      ? (request: Request) => fetch(request, { next: { revalidate, tags } } as RequestInit)
      : undefined,
  });
  // o IP do comprador para o rate limit do gateway (#176); só com o BFF configurado para isso, então
  // as páginas estáticas e os testes não leem os cabeçalhos da requisição
  if (env.BFF_GATEWAY_KEY && env.CLIENT_IP_HEADER) {
    const client: Middleware = {
      async onRequest({ request }) {
        for (const [name, value] of Object.entries(gatewayHeaders(await headers()))) {
          request.headers.set(name, value);
        }
        return request;
      },
    };
    api.use(client);
  }
  if (token) {
    const auth: Middleware = {
      onRequest({ request }) {
        request.headers.set("Authorization", `Bearer ${token}`);
        return request;
      },
    };
    api.use(auth);
  }
  return api;
}

export const api = {
  catalog: (options?: CallOptions) => client<catalog>("catalog", options),
  identity: (options?: CallOptions) => client<identity>("identity", options),
  inventory: (options?: CallOptions) => client<inventory>("inventory", options),
  orders: (options?: CallOptions) => client<orders>("orders", options),
  payments: (options?: CallOptions) => client<payments>("payments", options),
};

export type { components as CatalogSchemas } from "./schema/catalog";
export type { components as IdentitySchemas } from "./schema/identity";
export type { components as OrdersSchemas } from "./schema/orders";
