import type { Metadata } from "next";
import { connection } from "next/server";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { FilterChip } from "@/components/store/filter-chip";
import { ActiveFilters, ListingFilters } from "@/components/store/listing-filters";
import { ProductListing } from "@/components/store/product-listing";
import { categoryTree, getActiveCampaigns, getCategories } from "@/lib/catalog-api";
import { formatEnd, readFilters, withFilter } from "@/lib/storefront";

export const metadata: Metadata = {
  title: "Ofertas",
  description: "Todos os produtos com desconto de campanha agora, com o preço já baixado.",
  alternates: { canonical: "/ofertas" },
};

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** Tudo o que está com desconto de campanha agora, com filtro por departamento. */
export default async function Offers({ searchParams }: Props) {
  await connection();
  const params = await searchParams;
  const filters = readFilters(params);
  const { category } = filters;
  const [campaigns, categories] = await Promise.all([getActiveCampaigns(), getCategories()]);
  const roots = categories.ok ? categoryTree(categories.data) : [];

  return (
    <main id="conteudo" className="mx-auto max-w-6xl space-y-6 px-4 pt-6 sm:px-6">
      <Breadcrumbs items={[{ href: "/", label: "Início" }, { label: "Ofertas" }]} />
      <div className="space-y-2">
        <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Ofertas</h1>
        {campaigns.ok && campaigns.data.length > 0 && (
          <ul className="text-ink-soft space-y-1">
            {campaigns.data.map((c) => (
              <li key={c.id}>
                <span className="text-ink font-semibold">{c.name}</span>: até {c.discount_percent}% de
                desconto, até {formatEnd(c.ends_at)}
              </li>
            ))}
          </ul>
        )}
      </div>

      {roots.length > 0 && (
        <nav aria-label="Filtrar por departamento">
          <ul className="flex flex-wrap gap-2">
            <li>
              <FilterChip href={withFilter("/ofertas", params, "categoria", null)} active={!category}>
                Todos
              </FilterChip>
            </li>
            {roots.map((c) => (
              <li key={c.id}>
                <FilterChip
                  href={withFilter("/ofertas", params, "categoria", c.slug)}
                  active={category === c.slug}
                >
                  {c.name}
                </FilterChip>
              </li>
            ))}
          </ul>
        </nav>
      )}

      <div className="grid gap-6 lg:grid-cols-[15rem_minmax(0,1fr)] lg:gap-10">
        <ListingFilters
          path="/ofertas"
          params={params}
          filters={filters}
          keep={category ? { categoria: category } : {}}
          category={category}
          onSaleFixed
        />
        <div className="min-w-0 space-y-4">
          <ActiveFilters path="/ofertas" params={params} filters={filters} onSaleFixed />
          <ProductListing
            query={{
              onSale: true,
              category,
              brands: filters.brands,
              minPriceCents: filters.minPriceCents,
              maxPriceCents: filters.maxPriceCents,
              sort: filters.sort.api,
              page: filters.page,
            }}
            label="Ofertas"
            path={"/ofertas"}
            params={params}
            empty={{
              title: "Nenhuma oferta com esses filtros agora",
              description: "As campanhas mudam toda semana. Tire um filtro ou veja os outros departamentos.",
            }}
          />
        </div>
      </div>
    </main>
  );
}
