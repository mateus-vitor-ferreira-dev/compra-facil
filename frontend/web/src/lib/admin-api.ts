import "server-only";
import { updateTag } from "next/cache";
import { api, type CatalogSchemas, type FulfillmentSchemas } from "@/lib/api/client";
import type { components as InventorySchemas } from "@/lib/api/schema/inventory";

/**
 * As chamadas do painel (#154), sempre com o token do operador (papel `admin`) e sem cache: o painel
 * mostra o que está no banco agora. As escritas invalidam o cache de dados da vitrine.
 */

export type AdminProduct = CatalogSchemas["schemas"]["AdminProduct"];
export type PricingSettings = CatalogSchemas["schemas"]["PricingSettings"];
export type PricingRuleInput = CatalogSchemas["schemas"]["PricingRuleInput"];
export type CatalogSyncResult = FulfillmentSchemas["schemas"]["CatalogSyncResult"];
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

// ---------- mensagens com falha (dead letter topic) ----------

export type DeadLetter = InventorySchemas["schemas"]["DeadLetter"];

/** Os serviços que consomem eventos, cada um com a própria dead letter topic (ADR 0003). */
export const DLT_SERVICES = [
  {
    key: "orders",
    label: "Pedidos",
    consumes: "estoque reservado, resultado do pagamento, estorno e o andamento no fornecedor",
  },
  { key: "inventory", label: "Estoque", consumes: "pedidos feitos, confirmados e cancelados" },
  { key: "payments", label: "Pagamentos", consumes: "pedidos feitos e cancelados, estoque reservado" },
  { key: "fulfillment", label: "Fornecedor", consumes: "pedidos confirmados" },
  { key: "catalog", label: "Catálogo", consumes: "o feed do fornecedor" },
  {
    key: "notifications",
    label: "Avisos por e-mail",
    consumes: "pedidos confirmados e cancelados, contas novas",
  },
] as const;
export type DltService = (typeof DLT_SERVICES)[number]["key"];

// todos expõem as mesmas rotas (BuildingBlocks: MapDeadLetters); o tipo é o do inventory
function dltClient(token: string, service: DltService) {
  return api[service]({ token }) as unknown as ReturnType<typeof api.inventory>;
}

export function deadLetters(token: string, service: DltService) {
  return run(() =>
    dltClient(token, service).GET("/v1/admin/dead-letters", {
      params: { query: { limit: 100 } },
      cache: "no-store",
    }),
  );
}

/** Devolve as pendentes ao primeiro tópico de retry do serviço; falhando de novo, voltam para a DLT. */
export function replayDeadLetters(token: string, service: DltService) {
  return run(() => dltClient(token, service).POST("/v1/admin/dead-letters/replay", {}));
}

// ---------- pedidos atrasados no fornecedor ----------

export type OverdueOrder = FulfillmentSchemas["schemas"]["OverdueSupplierOrder"];

/**
 * Os pedidos parados além do prazo do fornecedor (despacho ou entrega), os mais atrasados primeiro, e o
 * momento da leitura (de quando o atraso é contado).
 */
export async function overdueOrders(token: string) {
  const result = await run(() =>
    api.fulfillment({ token }).GET("/v1/admin/supplier-orders/overdue", {
      params: { query: { limit: 200 } },
      cache: "no-store",
    }),
  );
  return { result, readAt: Date.now() };
}

// ---------- catálogo do fornecedor (#217) ----------

/** As regras de margem em vigor e o câmbio. */
export function pricingSettings(token: string) {
  return run(() => api.catalog({ token }).GET("/v1/admin/pricing-rules", { cache: "no-store" }));
}

/** Grava a regra (sem categoria: a padrão); o catalog reprecifica os produtos de fornecedor na hora. */
export async function setPricingRule(token: string, categoryId: string | null, body: PricingRuleInput) {
  const client = api.catalog({ token });
  const result = await run(() =>
    categoryId
      ? client.PUT("/v1/admin/pricing-rules/categories/{category_id}", {
          params: { path: { category_id: categoryId } },
          body,
        })
      : client.PUT("/v1/admin/pricing-rules/default", { body }),
  );
  if (result.ok && result.data.repriced > 0) refreshStorefront();
  return result;
}

export async function deletePricingRule(token: string, categoryId: string) {
  const result = await run(() =>
    api.catalog({ token }).DELETE("/v1/admin/pricing-rules/categories/{category_id}", {
      params: { path: { category_id: categoryId } },
    }),
  );
  if (result.ok && result.data.repriced > 0) refreshStorefront();
  return result;
}

/** Lê o feed do fornecedor agora; o que mudou chega ao catálogo pelos eventos, em segundos. */
export function syncSupplierCatalog(token: string) {
  return run(() => api.fulfillment({ token }).POST("/v1/admin/supplier-catalog/sync", {}));
}
