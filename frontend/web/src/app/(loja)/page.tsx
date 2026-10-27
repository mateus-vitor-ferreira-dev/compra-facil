import Link from "next/link";
import { connection } from "next/server";
import { CategoryIcon } from "@/components/commerce/category-icon";
import { CampaignHero } from "@/components/store/campaign-hero";
import { CatalogErrorState } from "@/components/store/catalog-error";
import { ProductGrid } from "@/components/store/product-grid";
import { brand } from "@/lib/brand";
import { categoryTree, getActiveCampaigns, getCategories, listProducts } from "@/lib/catalog-api";
import { featuredCampaign } from "@/lib/storefront";

/** A vitrine: a campanha vigente em destaque, as categorias e o que está em promoção agora. */
export default async function Home() {
  await connection(); // renderiza a cada requisição (com o cache de dados): a API não existe no build
  const [campaigns, categories, onSale] = await Promise.all([
    getActiveCampaigns(),
    getCategories(),
    listProducts({ onSale: true, limit: 8 }),
  ]);
  const featured = campaigns.ok ? featuredCampaign(campaigns.data) : undefined;
  const others = campaigns.ok && featured ? campaigns.data.filter((c) => c.id !== featured.id) : [];
  const tree = categories.ok ? categoryTree(categories.data) : [];

  return (
    <main id="conteudo">
      <h1 className="sr-only">
        {brand.name}: {brand.description}
      </h1>
      {featured && <CampaignHero campaign={featured} others={others} />}

      <div className="mx-auto max-w-6xl space-y-14 px-4 pt-10 sm:px-6">
        {tree.length > 0 && (
          <section aria-labelledby="categorias">
            <h2 id="categorias" className="text-xl font-semibold tracking-tight">
              Categorias
            </h2>
            <ul className="mt-4 grid grid-cols-3 gap-x-2 gap-y-4 sm:grid-cols-5 lg:grid-cols-6">
              {tree
                .flatMap((root) => [root, ...root.children])
                .map((c) => (
                  <li key={c.id}>
                    <Link
                      href={`/categoria/${c.slug}`}
                      className="group flex flex-col items-center gap-2 rounded-md py-2 text-center text-sm"
                    >
                      <span className="bg-photo text-ink group-hover:bg-ink group-hover:text-paper flex size-16 items-center justify-center rounded-full transition-colors">
                        <CategoryIcon slug={c.slug} className="size-7" />
                      </span>
                      <span className={c.parent_id ? "" : "font-semibold"}>{c.name}</span>
                    </Link>
                  </li>
                ))}
            </ul>
          </section>
        )}

        <section aria-labelledby="em-promocao">
          <div className="flex flex-wrap items-baseline justify-between gap-x-4 gap-y-1">
            <h2 id="em-promocao" className="text-xl font-semibold tracking-tight">
              Em promoção agora
            </h2>
            <Link
              href="/ofertas"
              className="inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
            >
              Ver todas as ofertas
            </Link>
          </div>
          <div className="mt-4">
            {onSale.ok ? (
              <ProductGrid
                products={onSale.data.data}
                label="Produtos em promoção"
                priorityCount={featured ? 0 : 4}
              />
            ) : (
              <CatalogErrorState error={onSale.error} retryHref="/" headingLevel={3} />
            )}
          </div>
        </section>
      </div>
    </main>
  );
}
