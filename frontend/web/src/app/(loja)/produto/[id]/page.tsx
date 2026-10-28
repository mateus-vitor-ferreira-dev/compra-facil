import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { cache } from "react";
import { z } from "zod";
import { PriceTag } from "@/components/commerce/price-tag";
import { ProductImage } from "@/components/commerce/product-image";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { CatalogErrorState } from "@/components/store/catalog-error";
import { Countdown } from "@/components/store/countdown";
import { ProductGrid } from "@/components/store/product-grid";
import { getCategories, getProduct, listProducts } from "@/lib/catalog-api";
import { categoryPath } from "@/lib/category-path";
import { env } from "@/lib/env";
import { discountPercent, formatMoney } from "@/lib/format";
import { getAvailability } from "@/lib/inventory-api";
import { AddToCartForm } from "./add-to-cart-form";

type Props = { params: Promise<{ id: string }> };

// a mesma requisição serve o generateMetadata e a página
const load = cache(async (id: string) => (z.uuid().safeParse(id).success ? getProduct(id) : null));

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const product = await load((await params).id);
  if (!product?.ok) return { title: "Produto" };
  const p = product.data;
  return {
    title: p.name,
    description: `${p.name} por ${formatMoney(p.effective_price)}. ${p.description ?? ""}`.trim(),
    alternates: { canonical: `/produto/${p.id}` },
    openGraph: {
      title: p.name,
      images: p.image
        ? [{ url: p.image.url, width: p.image.width ?? undefined, height: p.image.height ?? undefined }]
        : [],
    },
  };
}

/** "Últimas 3 unidades" só quando é pouco; sem resposta do estoque, a página não promete nada. */
function availabilityText(available: number | undefined) {
  if (available === undefined) return null;
  if (available <= 0) return { text: "Esgotado", tone: "text-danger" };
  if (available <= 5)
    return { text: `Últimas ${available} ${available === 1 ? "unidade" : "unidades"}`, tone: "text-ink" };
  return { text: "Em estoque", tone: "text-success" };
}

export default async function ProductPage({ params }: Props) {
  await connection();
  const { id } = await params;
  const product = await load(id);
  if (!product || (!product.ok && product.error.status === 404)) notFound();
  if (!product.ok) {
    return (
      <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
        <CatalogErrorState error={product.error} retryHref={`/produto/${id}`} />
      </main>
    );
  }

  const p = product.data;
  const [stock, categories, related] = await Promise.all([
    getAvailability([p.id]),
    getCategories(),
    listProducts({ category: p.category.slug, limit: 5 }),
  ]);
  const available = stock.ok ? stock.data[p.id] : undefined;
  const availability = availabilityText(available);
  // departamento › subcategoria › tipo (#210); o último é a categoria do produto
  const ancestors = categories.ok ? categoryPath(categories.data, p.category.id).slice(0, -1) : [];
  const more = related.ok ? related.data.data.filter((r) => r.id !== p.id).slice(0, 4) : [];
  const off = discountPercent(p.price, p.effective_price);

  const site = env.SITE_URL.replace(/\/$/, "");
  const jsonLd = {
    "@context": "https://schema.org",
    "@type": "Product",
    name: p.name,
    sku: p.sku,
    description: p.description,
    image: p.image?.url ?? undefined,
    category: p.category.name,
    offers: {
      "@type": "Offer",
      url: `${site}/produto/${p.id}`,
      priceCurrency: p.effective_price.currency ?? "BRL",
      price: (p.effective_price.amount_cents / 100).toFixed(2),
      ...(p.campaign ? { priceValidUntil: p.campaign.ends_at.slice(0, 10) } : {}),
      availability:
        available === undefined
          ? undefined
          : available > 0
            ? "https://schema.org/InStock"
            : "https://schema.org/OutOfStock",
    },
  };

  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
      <Breadcrumbs
        items={[
          { href: "/", label: "Início" },
          ...ancestors.map((c) => ({ href: `/categoria/${c.slug}`, label: c.name })),
          { href: `/categoria/${p.category.slug}`, label: p.category.name },
          { label: p.name },
        ]}
      />

      <div className="mt-4 grid gap-8 md:grid-cols-[minmax(0,7fr)_minmax(0,5fr)] md:gap-12">
        <ProductImage
          image={p.image}
          alt={p.name}
          categorySlug={p.category.slug}
          sizes="(min-width: 1152px) 640px, (min-width: 768px) 56vw, 100vw"
          priority
        />

        <div className="space-y-6 md:sticky md:top-6 md:self-start">
          <div className="space-y-3">
            <h1 className="text-xl leading-tight font-semibold tracking-tight sm:text-2xl">{p.name}</h1>
            <p className="text-ink-soft text-sm">Código {p.sku}</p>
          </div>

          <PriceTag price={p.price} effective={p.effective_price} size="lg" />

          {p.campaign && off > 0 && (
            // a tira do encarte: a mesma campanha da home, com o prazo deste preço
            <div className="bg-hero text-on-hero space-y-1 rounded-md px-4 py-3">
              <p className="font-semibold">
                {p.campaign.name}: {off}% de desconto neste produto
              </p>
              <Countdown endsAt={p.campaign.ends_at} className="text-on-hero/85 text-sm" />
            </div>
          )}

          {availability && <p className={`font-semibold ${availability.tone}`}>{availability.text}</p>}

          {available !== undefined && available <= 0 ? (
            <p className="text-ink-soft">
              Sem unidades para vender agora. Veja outros produtos de {p.category.name} logo abaixo.
            </p>
          ) : (
            <AddToCartForm productId={p.id} seenPriceCents={p.effective_price.amount_cents} max={available} />
          )}
        </div>
      </div>

      {p.description && (
        <section aria-labelledby="descricao" className="mt-12 max-w-prose">
          <h2 id="descricao" className="text-xl font-semibold tracking-tight">
            Descrição
          </h2>
          <p className="text-ink-soft mt-3 leading-relaxed whitespace-pre-line">{p.description}</p>
        </section>
      )}

      {more.length > 0 && (
        <section aria-labelledby="mais" className="mt-14">
          <h2 id="mais" className="text-xl font-semibold tracking-tight">
            Mais em {p.category.name}
          </h2>
          <ProductGrid products={more} label={`Mais em ${p.category.name}`} className="mt-4" />
        </section>
      )}

      <script
        type="application/ld+json"
        dangerouslySetInnerHTML={{ __html: JSON.stringify(jsonLd).replace(/</g, "\\u003c") }}
      />
    </main>
  );
}
