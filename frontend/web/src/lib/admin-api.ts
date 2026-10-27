import "server-only";
import { updateTag } from "next/cache";
import { api, type CatalogSchemas } from "@/lib/api/client";
import type { components as InventorySchemas } from "@/lib/api/schema/inventory";

/**
 * As chamadas do painel (#154), sempre com o token do operador (papel `admin`) e sem cache: o painel
 * mostra o que está no banco agora. As escritas invalidam o cache de dados da vitrine.
 */

export type AdminProduct = CatalogSchemas["schemas"]["AdminProduct"];
export type ProductInput = CatalogSchemas["schemas"]["ProductInput"];
export type Category = CatalogSchemas["schemas"]["Category"];
export type CategoryInput = CatalogSchemas["schemas"]["CategoryInput"];
export type StockLevel = InventorySchemas["schemas"]["StockLevel"];
export type StockAdjustment = InventorySchemas["schemas"]["StockAdjustment"];

/** O que deu errado, com o que o formulário precisa: os campos do 422 e o código para o suporte. */
export type AdminError = {
  status: number;
  detail?: string;
  fields?: Record<string, string>;
  traceId?: string;
};
export type AdminResult<T> = { ok: true; data: T; etag?: string } | { ok: false; error: AdminError };

type Problem = {
  detail?: string;
  trace_id?: string;
  errors?: { field: string; message: string }[];
};

async function run<T>(
  call: () => Promise<{ data?: T; error?: unknown; response: Response }>,
): Promise<AdminResult<T>> {
  try {
    const { data, error, response } = await call();
    if (response.ok) return { ok: true, data: data as T, etag: response.headers.get("etag") ?? undefined };
    const problem = (error ?? {}) as Problem;
    return {
      ok: false,
      error: {
        status: response.status,
        detail: problem.detail,
        traceId: problem.trace_id ?? response.headers.get("x-trace-id") ?? undefined,
        fields: problem.errors?.length
          ? Object.fromEntries(problem.errors.map((e) => [e.field, e.message]))
          : undefined,
      },
    };
  } catch {
    return { ok: false, error: { status: 503, detail: "o gateway não respondeu" } };
  }
}

/** A vitrine guarda produtos e categorias em cache por até 60 s: a escrita no painel aparece logo. */
function refreshStorefront(productId?: string) {
  updateTag("catalog:products");
  updateTag("catalog:categories");
  if (productId) updateTag(`catalog:product:${productId}`);
}

// ---------- produtos ----------

export type ProductFilter = { q?: string; status?: AdminProduct["status"]; cursor?: string };

export function listProducts(token: string, f: ProductFilter) {
  return run(() =>
    api.catalog({ token }).GET("/v1/admin/products", {
      params: { query: { q: f.q || undefined, status: f.status, cursor: f.cursor || undefined, limit: 50 } },
      cache: "no-store",
    }),
  );
}

export function getProduct(token: string, id: string) {
  return run(() =>
    api.catalog({ token }).GET("/v1/admin/products/{product_id}", {
      params: { path: { product_id: id } },
      cache: "no-store",
    }),
  );
}

export async function createProduct(token: string, body: ProductInput) {
  const result = await run(() => api.catalog({ token }).POST("/v1/admin/products", { body }));
  if (result.ok) refreshStorefront();
  return result;
}

/** Grava se ninguém alterou desde a leitura: `etag` é o da leitura; outro dá 412. */
export async function updateProduct(token: string, id: string, etag: string, body: ProductInput) {
  const result = await run(() =>
    api.catalog({ token }).PUT("/v1/admin/products/{product_id}", {
      params: { path: { product_id: id }, header: { "If-Match": etag } },
      body,
    }),
  );
  if (result.ok) refreshStorefront(id);
  return result;
}

export async function deleteProduct(token: string, id: string) {
  const result = await run(() =>
    api
      .catalog({ token })
      .DELETE("/v1/admin/products/{product_id}", { params: { path: { product_id: id } } }),
  );
  if (result.ok) refreshStorefront(id);
  return result;
}

// ---------- categorias ----------

export function listCategories() {
  return run(() => api.catalog().GET("/v1/categories", { cache: "no-store" }));
}

export async function createCategory(token: string, body: CategoryInput) {
  const result = await run(() => api.catalog({ token }).POST("/v1/admin/categories", { body }));
  if (result.ok) refreshStorefront();
  return result;
}

// ---------- estoque ----------

/** O saldo de cada produto (físico, reservado, disponível), por id; até 100 ids. */
export async function getStock(ids: string[]): Promise<Record<string, StockLevel>> {
  if (ids.length === 0) return {};
  const result = await run(() =>
    api.inventory().GET("/v1/stock", {
      params: { query: { product_ids: ids.slice(0, 100) } },
      cache: "no-store",
    }),
  );
  return result.ok ? Object.fromEntries(result.data.data.map((s) => [s.product_id, s])) : {};
}

/** Ajusta o estoque físico; a mesma `idempotencyKey` não ajusta duas vezes (clique duplo, reenvio). */
export async function adjustStock(token: string, id: string, idempotencyKey: string, body: StockAdjustment) {
  const result = await run(() =>
    api.inventory({ token }).POST("/v1/admin/stock/{product_id}/adjustments", {
      params: { path: { product_id: id }, header: { "Idempotency-Key": idempotencyKey } },
      body,
    }),
  );
  if (result.ok) updateTag("inventory:stock");
  return result;
}

// ---------- campanhas ----------

export type Campaign = CatalogSchemas["schemas"]["Campaign"];
export type CampaignInput = CatalogSchemas["schemas"]["CampaignInput"];

export function listCampaigns(token: string, cursor?: string) {
  return run(() =>
    api.catalog({ token }).GET("/v1/admin/campaigns", {
      params: { query: { cursor: cursor || undefined, limit: 50 } },
      cache: "no-store",
    }),
  );
}

export function getCampaign(token: string, id: string) {
  return run(() =>
    api.catalog({ token }).GET("/v1/admin/campaigns/{campaign_id}", {
      params: { path: { campaign_id: id } },
      cache: "no-store",
    }),
  );
}

export function campaignProducts(token: string, id: string) {
  return run(() =>
    api.catalog({ token }).GET("/v1/admin/campaigns/{campaign_id}/products", {
      params: { path: { campaign_id: id } },
      cache: "no-store",
    }),
  );
}

export async function createCampaign(token: string, body: CampaignInput) {
  const result = await run(() => api.catalog({ token }).POST("/v1/admin/campaigns", { body }));
  if (result.ok) refreshCampaigns();
  return result;
}

export async function updateCampaign(token: string, id: string, body: CampaignInput) {
  const result = await run(() =>
    api.catalog({ token }).PUT("/v1/admin/campaigns/{campaign_id}", {
      params: { path: { campaign_id: id } },
      body,
    }),
  );
  if (result.ok) refreshCampaigns();
  return result;
}

export async function deleteCampaign(token: string, id: string) {
  const result = await run(() =>
    api.catalog({ token }).DELETE("/v1/admin/campaigns/{campaign_id}", {
      params: { path: { campaign_id: id } },
    }),
  );
  if (result.ok) refreshCampaigns();
  return result;
}

/** Substitui a lista inteira de produtos da campanha (o contrato não tem "acrescentar um"). */
export async function setCampaignProducts(token: string, id: string, productIds: string[]) {
  const result = await run(() =>
    api.catalog({ token }).PUT("/v1/admin/campaigns/{campaign_id}/products", {
      params: { path: { campaign_id: id } },
      body: { product_ids: productIds },
    }),
  );
  if (result.ok) refreshCampaigns();
  return result;
}

/** Campanha muda o preço de muitos produtos de uma vez: a vitrine relê produtos e campanhas. */
function refreshCampaigns() {
  refreshStorefront();
  updateTag("catalog:campaigns");
}
