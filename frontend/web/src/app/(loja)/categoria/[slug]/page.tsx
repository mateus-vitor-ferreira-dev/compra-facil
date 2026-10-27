import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { buttonVariants } from "@/components/ui/button";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { CatalogErrorState } from "@/components/store/catalog-error";
import { FilterChip } from "@/components/store/filter-chip";
import { ProductListing } from "@/components/store/product-listing";
import { brand } from "@/lib/brand";
import { getCategories, type Category } from "@/lib/catalog-api";
import { readFilters, withFilter } from "@/lib/storefront";

type Props = {
  params: Promise<{ slug: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/** A categoria, a mãe dela (se for subcategoria) e as irmãs ou filhas, para os links de subcategoria. */
async function findCategory(slug: string) {
  const categories = await getCategories();
  if (!categories.ok) return { ok: false, error: categories.error } as const;
  const all = categories.data;
  const category = all.find((c) => c.slug === slug);
  if (!category) return { ok: true, category: undefined } as const;
  const parent = category.parent_id ? all.find((c) => c.id === category.parent_id) : undefined;
  const root: Category = parent ?? category;
  return {
    ok: true,
    category,
    parent,
    root,
    subcategories: all.filter((c) => c.parent_id === root.id),
  } as const;
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
  const { category, parent, root, subcategories } = found;

  const { onSale, cursor } = readFilters(query);

  return (
    <main id="conteudo" className="mx-auto max-w-6xl space-y-6 px-4 pt-6 sm:px-6">
      <Breadcrumbs
        items={[
          { href: "/", label: "Início" },
          ...(parent ? [{ href: `/categoria/${parent.slug}`, label: parent.name }] : []),
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
                    variant: c.id === category.id ? "primary" : "secondary",
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

      <div>
        <FilterChip href={withFilter(path, query, "promocao", onSale ? null : "1")} active={onSale}>
          Só em promoção
        </FilterChip>
      </div>

      <ProductListing
        query={{ category: slug, onSale, cursor }}
        label={`Produtos em ${category.name}`}
        selfHref={withFilter(path, query, "cursor", cursor ?? null)}
        nextHref={(next) => withFilter(path, query, "cursor", next)}
        empty={
          onSale
            ? {
                title: `Nada em promoção em ${category.name} agora`,
                description: "Tire o filtro para ver todos os produtos da categoria.",
                action: (
                  <Link href={path} className={buttonVariants({ variant: "secondary" })}>
                    Ver todos em {category.name}
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
    </main>
  );
}
