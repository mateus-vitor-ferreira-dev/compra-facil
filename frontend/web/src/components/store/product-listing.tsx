import { NotFoundIllustration } from "@/components/brand/illustrations";
import { StateMessage } from "@/components/ui/state";
import { listProducts, type ProductQuery } from "@/lib/catalog-api";
import { env } from "@/lib/env";
import { CatalogErrorState } from "./catalog-error";
import { LoadMore } from "./load-more";
import { ProductGrid } from "./product-grid";

/**
 * A grade de uma listagem (categoria, ofertas, busca) com o "Ver mais" por cursor, o estado vazio e o
 * JSON-LD ItemList para os buscadores.
 */
export async function ProductListing({
  query,
  label,
  selfHref,
  nextHref,
  empty,
}: {
  query: ProductQuery;
  label: string;
  /** a própria página, para o "Tentar de novo" */
  selfHref: string;
  /** a próxima página por link (sem JavaScript), a partir do cursor */
  nextHref: (cursor: string) => string;
  empty: { title: string; description: string; action?: React.ReactNode };
}) {
  const result = await listProducts(query);
  if (!result.ok) return <CatalogErrorState error={result.error} retryHref={selfHref} />;

  const { data: products, page } = result.data;
  if (products.length === 0) {
    return <StateMessage illustration={<NotFoundIllustration />} {...empty} />;
  }

  const site = env.SITE_URL.replace(/\/$/, "");
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "ItemList",
    itemListElement: products.map((p, i) => ({
      "@type": "ListItem",
      position: i + 1,
      url: `${site}/produto/${p.id}`,
      name: p.name,
    })),
  };
  const apiQuery: Record<string, string> = {};
  if (query.q) apiQuery.q = query.q;
  if (query.category) apiQuery.category = query.category;
  if (query.onSale) apiQuery.on_sale = "true";
  if (query.brands?.length) apiQuery.brand = query.brands.join(",");
  if (query.minPriceCents !== undefined) apiQuery.min_price_cents = String(query.minPriceCents);
  if (query.maxPriceCents !== undefined) apiQuery.max_price_cents = String(query.maxPriceCents);
  if (query.sort) apiQuery.sort = query.sort;

  return (
    <>
      {/* os cards são h3: o h2 da grade mantém a ordem dos títulos para quem navega por eles */}
      <h2 className="sr-only">{label}</h2>
      <ProductGrid products={products} label={label} priorityCount={query.cursor ? 0 : 4}>
        {page.has_more && page.next_cursor && (
          <LoadMore query={apiQuery} cursor={page.next_cursor} pageHref={nextHref(page.next_cursor)} />
        )}
      </ProductGrid>
      <script
        type="application/ld+json"
        // o "<" escapado impede que um nome de produto feche a tag
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }}
      />
    </>
  );
}
