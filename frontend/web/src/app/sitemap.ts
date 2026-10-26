import type { MetadataRoute } from "next";
import { connection } from "next/server";
import { getCategories } from "@/lib/catalog-api";
import { env } from "@/lib/env";

/**
 * A home, as ofertas e as categorias. Os produtos ficam de fora por enquanto: listar os 10 mil pela API
 * pública passaria do rate limit do gateway; os buscadores chegam a eles pelas categorias (ItemList).
 */
export default async function sitemap(): Promise<MetadataRoute.Sitemap> {
  await connection(); // gerado na requisição: a API não está disponível no build
  const site = env.SITE_URL.replace(/\/$/, "");
  const categories = await getCategories();
  return [
    { url: `${site}/`, changeFrequency: "daily", priority: 1 },
    { url: `${site}/ofertas`, changeFrequency: "daily", priority: 0.9 },
    ...(categories.ok ? categories.data : []).map((c) => ({
      url: `${site}/categoria/${c.slug}`,
      changeFrequency: "daily" as const,
      priority: c.parent_id ? 0.7 : 0.8,
    })),
  ];
}
