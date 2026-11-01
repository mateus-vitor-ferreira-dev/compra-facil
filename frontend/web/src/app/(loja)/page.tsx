import { Check } from "lucide-react";
import Link from "next/link";
import { connection } from "next/server";
import { CategoryIcon } from "@/components/commerce/category-icon";
import { CampaignHero } from "@/components/store/campaign-hero";
import { CatalogErrorState } from "@/components/store/catalog-error";
import { ProductGrid } from "@/components/store/product-grid";
import { brand } from "@/lib/brand";
import { MAX_INSTALLMENTS } from "@/lib/card";
import { categoryTree, getActiveCampaigns, getCategories, listProducts } from "@/lib/catalog-api";
import { featuredCampaign } from "@/lib/storefront";

/** A vitrine: o que é a loja, a campanha vigente, o que está em promoção agora e os departamentos. */
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
      {/* quem chega pela primeira vez lê o que é a loja; só o que ela já cumpre entra na lista */}
      <div className="mx-auto max-w-6xl px-4 py-3 sm:px-6">
        <h1 className="font-semibold sm:text-lg">
          <span className="sr-only">{brand.name}: </span>
          {brand.description}
        </h1>
        <ul className="text-ink-soft mt-1 flex flex-wrap gap-x-4 gap-y-1 text-sm">
          {[`PIX ou cartão em até ${MAX_INSTALLMENTS}x`, "7 dias para desistir", "Rastreio na sua conta"].map(
            (proof) => (
              <li key={proof} className="flex items-center gap-1">
                <Check aria-hidden="true" className="text-success size-4" />
                {proof}
              </li>
            ),
          )}
        </ul>
      </div>
      {featured && <CampaignHero campaign={featured} others={others} />}

      <div className="mx-auto max-w-6xl space-y-14 px-4 pt-6 sm:px-6 md:pt-10">
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
                priorityCount={featured ? 2 : 4}
              />
            ) : (
              <CatalogErrorState error={onSale.error} retryHref="/" headingLevel={3} />
            )}
          </div>
        </section>

        {/* só os departamentos: as subcategorias ficam dentro de cada um */}
        {tree.length > 0 && (
          <section aria-labelledby="categorias">
            <h2 id="categorias" className="text-xl font-semibold tracking-tight">
              Categorias
            </h2>
            <ul className="mt-4 grid grid-cols-3 gap-x-2 gap-y-4 sm:grid-cols-4 lg:grid-cols-6">
              {tree.map((c) => (
                <li key={c.id}>
                  <Link
                    href={`/categoria/${c.slug}`}
                    className="group flex flex-col items-center gap-2 rounded-md py-2 text-center text-sm font-semibold"
                  >
                    <span className="bg-photo text-ink group-hover:bg-ink group-hover:text-paper flex size-16 items-center justify-center rounded-full transition-colors">
                      <CategoryIcon slug={c.slug} className="size-7" />
                    </span>
                    {c.name}
                  </Link>
                </li>
              ))}
            </ul>
          </section>
        )}
      </div>
    </main>
  );
}
