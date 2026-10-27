import { NextResponse, type NextRequest } from "next/server";
import { listProducts, type ProductSort } from "@/lib/catalog-api";

const SORTS: ProductSort[] = ["relevance", "price_asc", "price_desc", "discount", "newest"];
const cents = (v: string | null) => (v && /^\d{1,12}$/.test(v) ? Number(v) : undefined);

/**
 * A próxima página de uma listagem, para o "Ver mais produtos" (o navegador não fala com a API
 * direto). Repassa só os filtros que a vitrine usa; o 429 do gateway volta com o Retry-After.
 */
export async function GET(request: NextRequest) {
  const p = request.nextUrl.searchParams;
  const result = await listProducts({
    q: p.get("q") ?? undefined,
    category: p.get("category") ?? undefined,
    onSale: p.get("on_sale") === "true",
    brands: p.get("brand")?.split(",").filter(Boolean).slice(0, 20),
    minPriceCents: cents(p.get("min_price_cents")),
    maxPriceCents: cents(p.get("max_price_cents")),
    sort: SORTS.find((s) => s === p.get("sort")),
    cursor: p.get("cursor") ?? undefined,
  });
  if (result.ok) return NextResponse.json(result.data);
  const headers = result.error.retryAfter ? { "Retry-After": String(result.error.retryAfter) } : undefined;
  return NextResponse.json({ status: result.error.status }, { status: result.error.status, headers });
}
