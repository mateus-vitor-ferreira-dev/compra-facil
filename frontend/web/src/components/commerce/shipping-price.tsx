import { formatMoney } from "@/lib/format";
import type { ShippingOption } from "@/lib/shipping";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/**
 * O preço de uma opção de frete. No frete grátis pela margem (#323), "Grátis" com o preço normal riscado ao lado:
 * quem compra vê o que economiza. Sem o preço normal (o fornecedor não cobra), só "Grátis".
 */
export function ShippingPrice({
  option,
}: {
  option: Pick<ShippingOption, "price_cents" | "regular_price_cents">;
}) {
  if (option.price_cents > 0) return <>{brl(option.price_cents)}</>;
  return (
    <span className="text-success inline-flex items-baseline gap-2">
      {option.regular_price_cents ? (
        <s className="text-ink-soft font-sans text-sm font-normal">
          <span className="sr-only">de </span>
          {brl(option.regular_price_cents)}
          <span className="sr-only"> por</span>
        </s>
      ) : null}
      Grátis
    </span>
  );
}
