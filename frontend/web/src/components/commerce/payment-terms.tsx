import { installmentOptions } from "@/lib/card";
import type { Money } from "@/lib/catalog";
import { formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

/**
 * A condição de pagamento junto do preço (#273): as mesmas parcelas sem juros que o checkout oferece, e o
 * preço à vista continua sendo o número grande. Sem desconto no PIX: a loja cobra o mesmo valor nos dois.
 */
export function PaymentTerms({
  price,
  size = "md",
  className,
}: {
  price: Money;
  size?: "md" | "lg";
  className?: string;
}) {
  const most = installmentOptions(price.amount_cents).at(-1);
  const each = most && most.count > 1 ? formatMoney({ ...price, amount_cents: most.eachCents }) : null;
  const text = each
    ? `${size === "lg" ? "à vista no PIX ou em até" : "no PIX ou"} ${most?.count}x de ${each} sem juros`
    : "à vista no PIX ou no cartão";

  return <p className={cn("text-ink-soft", size === "lg" ? "text-base" : "text-xs", className)}>{text}</p>;
}
