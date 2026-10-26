import { discountPercent, formatMoney, splitMoney } from "@/lib/format";
import type { Money } from "@/lib/catalog";
import { cn } from "@/lib/utils";

/**
 * O preço como no encarte: número grande e estreito, centavos em cima. Com campanha, o efetivo vai
 * numa etiqueta amarela (canto chanfrado e furo, o mesmo desenho do logo) e o de tabela, riscado,
 * acima. O leitor de tela ouve "de R$ 3.299,90 por R$ 2.639,90, 20% de desconto".
 */
export function PriceTag({
  price,
  effective,
  size = "md",
  className,
}: {
  price: Money;
  effective: Money;
  size?: "md" | "lg";
  className?: string;
}) {
  const off = discountPercent(price, effective);
  const { symbol, reais, centavos } = splitMoney(effective);
  const big = size === "lg" ? "text-4xl" : "text-2xl";
  const small = size === "lg" ? "text-lg" : "text-sm";

  const amount = (
    <span aria-hidden="true" className="font-condensed inline-flex items-start leading-none font-extrabold">
      <span className={cn(small, "mt-[0.15em] mr-0.5")}>{symbol}</span>
      <span className={big}>{reais}</span>
      <span className={cn(small, "mt-[0.15em] ml-0.5")}>{centavos}</span>
    </span>
  );

  if (off === 0) {
    return (
      <p className={cn("text-ink", className)}>
        <span className="sr-only">{formatMoney(effective)}</span>
        {amount}
      </p>
    );
  }

  return (
    <div className={cn("flex flex-col items-start gap-1", className)}>
      <p className="sr-only">
        de {formatMoney(price)} por {formatMoney(effective)}, {off}% de desconto
      </p>
      <s
        aria-hidden="true"
        className={cn("text-ink-soft tabular-nums", size === "lg" ? "text-base" : "text-sm")}
      >
        de {formatMoney(price)}
      </s>
      <span aria-hidden="true" className="inline-flex items-center gap-2">
        <span
          className={cn(
            "bg-tag text-tag-ink relative inline-flex items-center rounded-r-sm",
            size === "lg" ? "py-2 pr-4 pl-7" : "py-1.5 pr-3 pl-5",
          )}
          style={{ clipPath: "polygon(0.65rem 0, 100% 0, 100% 100%, 0 100%, 0 0.65rem)" }}
        >
          {/* o furo da etiqueta */}
          <span className="bg-paper absolute top-1/2 left-2 size-1.5 -translate-y-1/2 rounded-full" />
          {amount}
        </span>
        <span className="font-condensed text-ink text-sm font-bold">-{off}%</span>
      </span>
    </div>
  );
}
