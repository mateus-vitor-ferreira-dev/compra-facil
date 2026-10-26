import { formatMoney } from "@/lib/format";

type Money = { amount_cents: number; currency?: string };

/** Preço do produto: o efetivo e, com campanha, o de tabela riscado. */
export function Price({ price, effective }: { price: Money; effective: Money }) {
  const discounted = effective.amount_cents < price.amount_cents;
  return (
    <span className="inline-flex items-baseline gap-2 tabular-nums">
      {discounted && (
        <s className="text-muted-foreground text-sm">
          <span className="sr-only">de </span>
          {formatMoney(price)}
        </s>
      )}
      <span>
        {discounted && <span className="sr-only">por </span>}
        {formatMoney(effective)}
      </span>
    </span>
  );
}
