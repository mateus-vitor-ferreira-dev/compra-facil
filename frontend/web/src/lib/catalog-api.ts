import "server-only";
import { api } from "@/lib/api/client";
import type { components } from "@/lib/api/schema/catalog";
import type { Product } from "@/lib/catalog";

export type Category = components["schemas"]["Category"];
export type Campaign = components["schemas"]["Campaign"];
export type ProductPage = components["schemas"]["ProductPage"];

/** O que deu errado ao falar com o catálogo, para a tela explicar (o 429 tem o próprio texto). */
export type CatalogError = { status: number; retryAfter?: number };
export type Result<T> = { ok: true; data: T } | { ok: false; error: CatalogError };

// o catálogo muda pouco e o preço de campanha muda nas viradas: 60 s de cache de dados bastam
const PRODUCTS_TTL = 60;
const CATEGORIES_TTL = 300;

export async function call<T>(run: () => Promise<{ data?: T; response: Response }>): Promise<Result<T>> {
  try {
    const { data, response } = await run();
    if (data !== undefined) return { ok: true, data };
    const retry = Number(response.headers.get("retry-after"));
    return {
      ok: false,
      error: { status: response.status, retryAfter: Number.isFinite(retry) && retry > 0 ? retry : undefined },
    };
  } catch {
    return { ok: false, error: { status: 503 } }; // gateway fora do ar
  }
}

export function getCategories(): Promise<Result<Category[]>> {
  return call(async () => {
    const r = await api
      .catalog({ revalidate: CATEGORIES_TTL, tags: ["catalog:categories"] })
      .GET("/v1/categories");
    return { data: r.data?.data, response: r.response };
  });
}

export function getActiveCampaigns(): Promise<Result<Campaign[]>> {
  return call(async () => {
    const r = await api
      .catalog({ revalidate: PRODUCTS_TTL, tags: ["catalog:campaigns"] })
      .GET("/v1/campaigns");
    return { data: r.data?.data, response: r.response };
  });
}

export function getProduct(id: string): Promise<Result<Product>> {
  return call(async () => {
    const r = await api
      .catalog({ revalidate: PRODUCTS_TTL, tags: ["catalog:products", `catalog:product:${id}`] })
      .GET("/v1/products/{product_id}", { params: { path: { product_id: id } } });
    return { data: r.data, response: r.response };
  });
}

export type BrandCount = components["schemas"]["BrandCount"];
export type ProductSort = components["schemas"]["ProductSort"];

export type ProductQuery = {
  q?: string;
  category?: string;
  onSale?: boolean;
  brands?: string[];
  minPriceCents?: number;
  maxPriceCents?: number;
  sort?: ProductSort;
  cursor?: string;
  limit?: number;
};

/** Os filtros da listagem como a API recebe (os mesmos em /v1/products e /v1/brands). */
function filterParams(query: ProductQuery) {
  return {
    q: query.q || undefined,
    category: query.category || undefined,
    on_sale: query.onSale || undefined,
    min_price_cents: query.minPriceCents,
    max_price_cents: query.maxPriceCents,
  };
}

export function listProducts(query: ProductQuery): Promise<Result<ProductPage>> {
  return call(async () => {
    const r = await api
      .catalog({ revalidate: PRODUCTS_TTL, tags: ["catalog:products"] })
      .GET("/v1/products", {
        params: {
          query: {
            ...filterParams(query),
            brand: query.brands?.length ? query.brands : undefined,
            sort: query.sort,
            cursor: query.cursor || undefined,
            limit: query.limit ?? 24,
          },
        },
        // o contrato pede as marcas separadas por vírgula (brand=Dell,Acer)
        querySerializer: { array: { style: "form", explode: false } },
      });
    return { data: r.data, response: r.response };
  });
}

/** As marcas da listagem com a contagem (o filtro de marca), pelos mesmos filtros, sem o de marca. */
export function listBrands(query: ProductQuery): Promise<Result<BrandCount[]>> {
  return call(async () => {
    const r = await api
      .catalog({ revalidate: PRODUCTS_TTL, tags: ["catalog:products"] })
      .GET("/v1/brands", { params: { query: filterParams(query) } });
    return { data: r.data?.data, response: r.response };
  });
}

/** Árvore de categorias: as raízes e as filhas de cada uma (a API devolve a lista plana). */
export function categoryTree(categories: Category[]) {
  const roots = categories.filter((c) => !c.parent_id);
  return roots.map((root) => ({ ...root, children: categories.filter((c) => c.parent_id === root.id) }));
}
