import { NextResponse, type NextRequest } from "next/server";
import type { Suggestion } from "@/components/store/search-box";
import { getCategories, listProducts } from "@/lib/catalog-api";
import { normalize } from "@/lib/storefront";

/**
 * Sugestões da busca do cabeçalho: até 2 categorias cujo nome casa com o texto e os 5 primeiros
 * produtos da busca do catálogo. O catálogo não tem endpoint de sugestões; as duas chamadas ficam no
 * cache de dados, então a mesma digitação não volta à API.
 */
export async function GET(request: NextRequest) {
  const q = request.nextUrl.searchParams.get("q")?.trim() ?? "";
  if (q.length < 2) return NextResponse.json({ data: [] });

  const [categories, products] = await Promise.all([getCategories(), listProducts({ q, limit: 5 })]);
  const term = normalize(q);
  const data: Suggestion[] = [
    ...(categories.ok ? categories.data : [])
      .filter((c) => normalize(c.name).includes(term))
      .slice(0, 2)
      .map((c) => ({ kind: "category" as const, slug: c.slug, name: c.name })),
    ...(products.ok ? products.data.data : []).map((p) => ({
      kind: "product" as const,
      id: p.id,
      name: p.name,
      category: p.category.name,
    })),
  ];
  return NextResponse.json({ data }, { headers: { "Cache-Control": "private, max-age=60" } });
}
