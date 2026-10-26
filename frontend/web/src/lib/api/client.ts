import "server-only";
import createClient, { type Middleware } from "openapi-fetch";
import { env } from "@/lib/env";
import type { paths as catalog } from "./schema/catalog";
import type { paths as identity } from "./schema/identity";
import type { paths as inventory } from "./schema/inventory";
import type { paths as orders } from "./schema/orders";
import type { paths as payments } from "./schema/payments";

/**
 * Clientes tipados das APIs (tipos gerados de contracts/openapi), sempre pelo gateway:
 * `${API_URL}/<serviço>/v1/...`. Com `token`, mandam o access token do cliente logado.
 */
function client<Paths extends object>(service: string, token?: string) {
  const api = createClient<Paths>({ baseUrl: `${env.API_URL.replace(/\/$/, "")}/${service}` });
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
  catalog: (token?: string) => client<catalog>("catalog", token),
  identity: (token?: string) => client<identity>("identity", token),
  inventory: (token?: string) => client<inventory>("inventory", token),
  orders: (token?: string) => client<orders>("orders", token),
  payments: (token?: string) => client<payments>("payments", token),
};

export type { components as CatalogSchemas } from "./schema/catalog";
export type { components as IdentitySchemas } from "./schema/identity";
export type { components as OrdersSchemas } from "./schema/orders";
