import { NextResponse } from "next/server";
import { z } from "zod";
import { quoteShipping } from "@/lib/shipping-api";

const body = z.object({
  zip_code: z
    .string()
    .transform((v) => v.replace(/\D/g, ""))
    .pipe(z.string().length(8)),
  items: z
    .array(z.object({ sku: z.string().min(1).max(64), quantity: z.number().int().min(1).max(99) }))
    .min(1)
    .max(50),
});

/**
 * O frete e o prazo por CEP (#222) para a página do produto, o carrinho e o checkout: o navegador manda o CEP
 * e os itens, o BFF cota no fulfillment. 422: CEP ou itens inválidos; 503: o fornecedor não cotou agora.
 */
export async function POST(request: Request) {
  const parsed = body.safeParse(await request.json().catch(() => null));
  if (!parsed.success) return NextResponse.json({ error: "CEP com 8 dígitos e os itens" }, { status: 422 });
  const quote = await quoteShipping(parsed.data.zip_code, parsed.data.items);
  if (!quote.ok) {
    return NextResponse.json(
      { error: "a cotação de frete não respondeu" },
      {
        status: quote.status === 422 ? 422 : 503,
        headers: quote.retryAfter ? { "Retry-After": String(quote.retryAfter) } : {},
      },
    );
  }
  return NextResponse.json(
    { zip_code: quote.zipCode, options: quote.options },
    { headers: { "Cache-Control": "private, max-age=300" } },
  );
}
