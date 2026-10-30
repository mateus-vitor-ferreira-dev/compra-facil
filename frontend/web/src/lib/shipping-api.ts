import "server-only";
import { api } from "@/lib/api/client";
import type { ShippingItem, ShippingOption } from "@/lib/shipping";

export type ShippingQuoteResult =
  | { ok: true; zipCode: string; options: ShippingOption[] }
  | { ok: false; status: number; retryAfter?: number };

/** A cotação de frete do fulfillment (#222), sem cache: o preço do fornecedor muda e cada CEP é um. */
export async function quoteShipping(zipCode: string, items: ShippingItem[]): Promise<ShippingQuoteResult> {
  try {
    const r = await api.fulfillment().POST("/v1/shipping/quote", {
      body: { zip_code: zipCode, items },
      cache: "no-store",
    });
    if (r.data) return { ok: true, zipCode: r.data.zip_code, options: r.data.options };
    const retry = Number(r.response.headers.get("retry-after"));
    return {
      ok: false,
      status: r.response.status,
      retryAfter: Number.isFinite(retry) && retry > 0 ? retry : undefined,
    };
  } catch {
    return { ok: false, status: 503 };
  }
}
