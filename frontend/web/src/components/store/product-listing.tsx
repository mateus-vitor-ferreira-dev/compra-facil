import Link from "next/link";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { listProducts, type ProductQuery } from "@/lib/catalog-api";
import { env } from "@/lib/env";
import { pageCount } from "@/lib/pagination";
import { withFilter } from "@/lib/storefront";
import { BatchFocus } from "./batch-focus";
import { CatalogErrorState } from "./catalog-error";
import { ProductGrid } from "./product-grid";

type Params = Record<string, string | string[] | undefined>;

// produtos por lote do "Carregar mais": 12 linhas de 4 no desktop (o Baymard recomenda 50 a 100 em
// listas técnicas) e cabe numa ida só ao catálogo (o limite dele é 100)
export const PER_PAGE = 48;

/**
 * quantos lotes uma URL renderiza de uma vez (?pagina=10 mostra 480 produtos): passar disso é rolar
 * demais, e a busca e os filtros servem melhor; a página continua indo até o fim, de 10 em 10 lotes
 */
const MAX_BATCHES = 10;

const count = new Intl.NumberFormat("pt-BR");

/**
 * A grade de uma listagem (categoria, ofertas, busca), no padrão "Carregar mais" do Baymard: o botão é
 * um link para ?pagina=N+1, e a página com ?pagina=N mostra os lotes 1 a N. Assim a URL acompanha o que
 * foi carregado, o Voltar da página do produto devolve ao mesmo ponto (o Next restaura a rolagem), e
 * sem JavaScript ou para um buscador o link funciona igual. Também o estado vazio e o JSON-LD ItemList.
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
  /** a própria listagem e os parâmetros dela, para o link do próximo lote */
  path: string;
  params: Params;
  empty: { title: string; description: string; action?: React.ReactNode };
}) {
  const requested = query.page ?? 1;
  // além de MAX_BATCHES, a URL mostra a última janela de lotes (?pagina=14 mostra os lotes 5 a 14)
  const firstBatch = Math.max(1, requested - MAX_BATCHES + 1);
  const batches = Array.from({ length: requested - firstBatch + 1 }, (_, i) => firstBatch + i);
  // o primeiro lote é a mesma chamada dos filtros (o total do botão da folha): o Next a faz uma vez só
  const results = await Promise.all(batches.map((page) => listProducts({ ...query, page, limit: PER_PAGE })));
  const failed = results.find((r) => !r.ok);
  const self = withFilter(path, params, "pagina", requested === 1 ? null : String(requested));
  if (failed && !failed.ok) return <CatalogErrorState error={failed.error} retryHref={self} />;

  const pages = results.flatMap((r) => (r.ok ? [r.data] : []));
  const total = pages[0]?.total ?? pages[0]?.data.length ?? 0;
  const products = pages.flatMap((p) => p.data);
  const batchCount = pageCount(total, PER_PAGE);
  // a URL pediu mais lotes do que há (a listagem encolheu desde o link): mostra o que existe
  const loaded = Math.min(requested, Math.max(batchCount, 1));
  if (products.length === 0 && total > 0) {
    // a URL pediu lotes que não existem mais (a listagem encolheu desde o link)
    return (
      <StateMessage
        illustration={<NotFoundIllustration />}
        title={`Esta listagem tem ${count.format(total)} ${total === 1 ? "produto" : "produtos"}`}
        description="O ponto da lista que você abriu não existe mais: os produtos mudaram desde o link."
        action={
          <Link
            href={withFilter(path, params, "pagina", null)}
            className={buttonVariants({ variant: "secondary" })}
          >
            Ver desde o começo
          </Link>
        }
      />
    );
  }
  if (products.length === 0) {
    return <StateMessage illustration={<NotFoundIllustration />} {...empty} />;
  }

  const first = (firstBatch - 1) * PER_PAGE;
  const seen = first + products.length;
  const remaining = total - seen;
  const site = env.SITE_URL.replace(/\/$/, "");
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "ItemList",
    numberOfItems: total,
    itemListElement: products.map((p, i) => ({
      "@type": "ListItem",
      position: first + i + 1,
      url: `${site}/produto/${p.id}`,
      name: p.name,
    })),
  };

  return (
    <>
      <p className="text-ink-soft text-sm">
        <span className="font-condensed text-ink text-base font-bold tabular-nums">
          {count.format(total)}
        </span>{" "}
        {total === 1 ? "produto" : "produtos"}
      </p>
      {/* os cards são h3: o h2 da grade mantém a ordem dos títulos para quem navega por eles */}
      <h2 className="sr-only">{label}</h2>
      {firstBatch > 1 && (
        <Link
          href={withFilter(path, params, "pagina", null)}
          className="inline-flex min-h-11 items-center text-sm font-semibold underline underline-offset-4"
        >
          Voltar ao começo da lista
        </Link>
      )}
      <ProductGrid
        products={products}
        label={label}
        priorityCount={firstBatch === 1 ? 4 : 0}
        batchSize={PER_PAGE}
      />
      <BatchFocus loaded={loaded} />
      {/* o total também no fim da lista, e quanto já foi visto (Baymard) */}
      <div className="mx-auto flex max-w-sm flex-col items-center gap-3 pt-4 text-center">
        <p className="text-ink-soft text-sm" aria-live="polite">
          Você viu {count.format(seen)} de {count.format(total)} {total === 1 ? "produto" : "produtos"}
        </p>
        <div aria-hidden="true" className="bg-ink/10 h-1 w-full overflow-hidden rounded-full">
          <div
            className="bg-ink h-full rounded-full"
            style={{ width: `${Math.min(100, (seen / total) * 100)}%` }}
          />
        </div>
        {remaining > 0 && (
          <Link
            href={withFilter(path, params, "pagina", String(loaded + 1))}
            scroll={false}
            prefetch={false}
            className={buttonVariants({ variant: "secondary", className: "w-full" })}
          >
            Carregar mais {count.format(Math.min(PER_PAGE, remaining))}{" "}
            {Math.min(PER_PAGE, remaining) === 1 ? "produto" : "produtos"}
          </Link>
        )}
      </div>
      <script
        type="application/ld+json"
        // o "<" escapado impede que um nome de produto feche a tag
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }}
      />
    </>
  );
}
