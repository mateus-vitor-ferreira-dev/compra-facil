import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { ActiveFilters, ListingFilters } from "@/components/store/listing-filters";
import { ProductListing } from "@/components/store/product-listing";
import { categoryTree, getCategories } from "@/lib/catalog-api";
import { activeFilterCount, readFilters, withFilter } from "@/lib/storefront";

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

export async function generateMetadata({ searchParams }: Props): Promise<Metadata> {
  const { q } = readFilters(await searchParams);
  // resultado de busca não vai para o índice: são infinitas URLs com o mesmo conteúdo
  return { title: q ? `Busca por “${q}”` : "Busca", robots: { index: false, follow: true } };
}

export default async function SearchPage({ searchParams }: Props) {
  await connection();
  const params = await searchParams;
  const filters = readFilters(params);
  const { q, onSale, cursor } = filters;
  const filtered = activeFilterCount(filters) > 0;
  const categories = await getCategories();
  const roots = categories.ok ? categoryTree(categories.data) : [];
  // sem resultado não é beco sem saída: as ofertas e os departamentos ficam a um toque
  const exits = (
    <ul className="flex flex-wrap gap-2">
      <li>
        <Link href="/ofertas" className={buttonVariants({ variant: "primary", size: "sm" })}>
          Ver as ofertas
        </Link>
      </li>
      {roots.map((c) => (
        <li key={c.id}>
          <Link
            href={`/categoria/${c.slug}`}
            className={buttonVariants({ variant: "secondary", size: "sm" })}
          >
            {c.name}
          </Link>
        </li>
      ))}
    </ul>
  );

  if (!q || q.length < 2) {
    return (
      <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
        <h1 className="sr-only">Busca</h1>
        <StateMessage
          illustration={<NotFoundIllustration />}
          title="Digite pelo menos 2 letras"
          description="Busque pelo nome do produto, pela marca ou pela categoria, como “notebook” ou “geladeira”."
          action={exits}
        />
      </main>
    );
  }

  return (
    <main id="conteudo" className="mx-auto max-w-6xl space-y-6 px-4 pt-6 sm:px-6">
      <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Resultados para “{q}”</h1>
      <div className="grid gap-6 lg:grid-cols-[15rem_minmax(0,1fr)] lg:gap-10">
        <ListingFilters path="/busca" params={params} filters={filters} keep={{ q }} />
        <div className="min-w-0 space-y-4">
          <ActiveFilters path="/busca" params={params} filters={filters} />
          <ProductListing
            query={{
              q,
              onSale,
              brands: filters.brands,
              minPriceCents: filters.minPriceCents,
              maxPriceCents: filters.maxPriceCents,
              sort: filters.sort.api,
              cursor,
            }}
            label={`Resultados para ${q}`}
            selfHref={withFilter("/busca", params, "cursor", cursor ?? null)}
            nextHref={(next) => withFilter("/busca", params, "cursor", next)}
            empty={{
              title: `Nenhum produto encontrado para “${q}”`,
              description: filtered
                ? "Nada com esses filtros. Tire uma marca, amplie a faixa de preço ou limpe os filtros."
                : "Confira a grafia, tente só a marca ou o tipo do produto, ou comece por um departamento.",
              action: exits,
            }}
          />
        </div>
      </div>
    </main>
  );
}
