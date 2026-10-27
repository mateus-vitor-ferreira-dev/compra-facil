import Link from "next/link";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { listProducts, type ProductQuery } from "@/lib/catalog-api";
import { env } from "@/lib/env";
import { pageCount } from "@/lib/pagination";
import { withFilter } from "@/lib/storefront";
import { CatalogErrorState } from "./catalog-error";
import { Pagination } from "./pagination";
import { ProductGrid } from "./product-grid";

type Params = Record<string, string | string[] | undefined>;

// produtos por página: 4 linhas de 6 no desktop, 12 de 2 no celular
export const PER_PAGE = 24;

const count = new Intl.NumberFormat("pt-BR");

/**
 * A grade de uma listagem (categoria, ofertas, busca): quantos produtos há, a página atual e a
 * paginação numerada (?pagina=), o estado vazio e o JSON-LD ItemList para os buscadores.
 */
export async function ProductListing({
  query,
  label,
  path,
  params,
  empty,
}: {
  query: ProductQuery;
  label: string;
  /** a própria listagem e os parâmetros dela, para os links das páginas */
  path: string;
  params: Params;
  empty: { title: string; description: string; action?: React.ReactNode };
}) {
  const current = query.page ?? 1;
  const result = await listProducts({ ...query, page: current, limit: PER_PAGE });
  const self = withFilter(path, params, "pagina", current === 1 ? null : String(current));
  if (!result.ok) return <CatalogErrorState error={result.error} retryHref={self} />;

  const { data: products } = result.data;
  const total = result.data.total ?? products.length;
  const pages = pageCount(total, PER_PAGE);
  if (products.length === 0 && total > 0) {
    // a página pedida passou do fim (a listagem encolheu desde o link)
    return (
      <StateMessage
        illustration={<NotFoundIllustration />}
        title={`Esta listagem tem ${pages === 1 ? "1 página" : `${pages} páginas`}`}
        description="A página que você abriu não existe mais: os produtos mudaram desde o link."
        action={
          <Link
            href={withFilter(path, params, "pagina", null)}
            className={buttonVariants({ variant: "secondary" })}
          >
            Ir para a primeira página
          </Link>
        }
      />
    );
  }
  if (products.length === 0) {
    return <StateMessage illustration={<NotFoundIllustration />} {...empty} />;
  }

  const site = env.SITE_URL.replace(/\/$/, "");
  const first = (current - 1) * PER_PAGE;
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "ItemList",
    itemListElement: products.map((p, i) => ({
      "@type": "ListItem",
      position: first + i + 1,
      url: `${site}/produto/${p.id}`,
      name: p.name,
    })),
  };

  return (
    <>
      <p className="text-ink-soft text-sm" aria-live="polite">
        <span className="font-condensed text-ink text-base font-bold tabular-nums">
          {count.format(total)}
        </span>{" "}
        {total === 1 ? "produto" : "produtos"}
        {pages > 1 && (
          <>
            , mostrando {count.format(first + 1)} a {count.format(first + products.length)}
          </>
        )}
      </p>
      {/* os cards são h3: o h2 da grade mantém a ordem dos títulos para quem navega por eles */}
      <h2 className="sr-only">{pages > 1 ? `${label}, página ${current} de ${pages}` : label}</h2>
      <ProductGrid products={products} label={label} priorityCount={current === 1 ? 4 : 0} />
      <Pagination path={path} params={params} current={current} total={pages} />
      <script
        type="application/ld+json"
        // o "<" escapado impede que um nome de produto feche a tag
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }}
      />
    </>
  );
}
