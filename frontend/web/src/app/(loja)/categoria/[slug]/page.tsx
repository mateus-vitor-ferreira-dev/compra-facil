import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { buttonVariants } from "@/components/ui/button";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { CatalogErrorState } from "@/components/store/catalog-error";
import { ActiveFilters, ListingFilters } from "@/components/store/listing-filters";
import { ProductListing } from "@/components/store/product-listing";
import { brand } from "@/lib/brand";
import { getCategories, type Category } from "@/lib/catalog-api";
import { categoryPath } from "@/lib/category-path";
import { activeFilterCount, readFilters, withoutRefinements } from "@/lib/storefront";

type Props = {
  params: Promise<{ slug: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/**
 * A categoria e a cadeia até o departamento, as subcategorias do departamento e, dentro de uma
 * subcategoria, os tipos dela (#210): os dois níveis de chips da página.
 */
async function findCategory(slug: string) {
  const categories = await getCategories();
  if (!categories.ok) return { ok: false, error: categories.error } as const;
  const all = categories.data;
  const category = all.find((c) => c.slug === slug);
  if (!category) return { ok: true, category: undefined } as const;
  const path = categoryPath(all, category.id);
  const root: Category = path[0] ?? category;
  const group: Category | undefined = path[1];
  return {
    ok: true,
    category,
    path,
    root,
    group,
    subcategories: all.filter((c) => c.parent_id === root.id),
    types: group ? all.filter((c) => c.parent_id === group.id) : [],
  } as const;
}

/** O chip de um tipo: mais leve que o da subcategoria, para os dois níveis não competirem. */
function chip(active: boolean) {
  return active
    ? "bg-ink text-paper inline-flex min-h-11 items-center rounded-full px-4 text-sm font-medium"
    : "bg-ink/6 hover:bg-ink/12 text-ink inline-flex min-h-11 items-center rounded-full px-4 text-sm";
}

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { slug } = await params;
  const found = await findCategory(slug);
  if (!found.ok || !found.category) return { title: "Categoria" };
  return {
    title: found.category.name,
    description: `${found.category.name} na ${brand.name}, com os preços de campanha já na etiqueta.`,
    alternates: { canonical: `/categoria/${slug}` },
  };
}

export default async function CategoryPage({ params, searchParams }: Props) {
  await connection();
  const [{ slug }, query] = await Promise.all([params, searchParams]);
  const path = `/categoria/${slug}`;
  const found = await findCategory(slug);
  if (!found.ok) {
    return (
      <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
        <CatalogErrorState error={found.error} retryHref={path} />
      </main>
    );
  }
  if (!found.category) notFound();
  const { category, path: chain, root, group, subcategories, types } = found;

  const filters = readFilters(query);
  const { onSale } = filters;
  const filtered = activeFilterCount(filters) > 0;

  return (
    <main id="conteudo" className="mx-auto max-w-6xl space-y-6 px-4 pt-6 sm:px-6">
      <Breadcrumbs
        items={[
          { href: "/", label: "Início" },
          ...chain.slice(0, -1).map((c) => ({ href: `/categoria/${c.slug}`, label: c.name })),
          { label: category.name },
        ]}
      />
      <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{category.name}</h1>

      {subcategories.length > 0 && (
        <nav aria-label={`Subcategorias de ${root.name}`}>
          <ul className="flex flex-wrap gap-2">
            <li>
              <Link
                href={`/categoria/${root.slug}`}
                aria-current={category.id === root.id ? "page" : undefined}
                className={buttonVariants({
                  variant: category.id === root.id ? "primary" : "secondary",
                  size: "sm",
                })}
              >
                Tudo em {root.name}
              </Link>
            </li>
            {subcategories.map((c) => (
              <li key={c.id}>
                <Link
                  href={`/categoria/${c.slug}`}
                  aria-current={c.id === category.id ? "page" : undefined}
                  className={buttonVariants({
                    // num tipo, a subcategoria dele continua marcada
                    variant: c.id === group?.id ? "primary" : "secondary",
                    size: "sm",
                  })}
                >
                  {c.name}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}

      {group && types.length > 0 && (
        <nav aria-label={`Tipos de ${group.name}`}>
          <ul className="flex flex-wrap gap-2">
            <li>
              <Link
                href={`/categoria/${group.slug}`}
                aria-current={category.id === group.id ? "page" : undefined}
                className={chip(category.id === group.id)}
              >
                Tudo em {group.name}
              </Link>
            </li>
            {types.map((c) => (
              <li key={c.id}>
                <Link
                  href={`/categoria/${c.slug}`}
                  aria-current={c.id === category.id ? "page" : undefined}
                  className={chip(c.id === category.id)}
                >
                  {c.name}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}

      <div className="grid gap-6 lg:grid-cols-[15rem_minmax(0,1fr)] lg:gap-10">
        <ListingFilters path={path} params={query} filters={filters} keep={{}} category={slug} />
        <div className="min-w-0 space-y-4">
          <ActiveFilters path={path} params={query} filters={filters} />
          <ProductListing
            query={{
              category: slug,
              onSale,
              brands: filters.brands,
              minPriceCents: filters.minPriceCents,
              maxPriceCents: filters.maxPriceCents,
              sort: filters.sort.api,
              page: filters.page,
            }}
            label={`Produtos em ${category.name}`}
            path={path}
            params={query}
            empty={
              filtered
                ? {
                    title: `Nenhum produto em ${category.name} com esses filtros`,
                    description: "Tire uma marca, amplie a faixa de preço ou limpe os filtros.",
                    action: (
                      <Link
                        href={withoutRefinements(path, query)}
                        className={buttonVariants({ variant: "secondary" })}
                      >
                        Limpar filtros
                      </Link>
                    ),
                  }
                : {
                    title: `Ainda não há produtos em ${category.name}`,
                    description: "Veja as outras categorias ou as ofertas da semana.",
                    action: (
                      <Link href="/ofertas" className={buttonVariants({ variant: "secondary" })}>
                        Ver as ofertas
                      </Link>
                    ),
                  }
            }
          />
        </div>
      </div>
    </main>
  );
}
