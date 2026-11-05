import type { SalesSummary } from "@/lib/admin-api";
import { formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";

type Method = SalesSummary["by_payment_method"][number]["method"];

// a cor segue o meio, não a posição: o PIX é sempre o anil, o cartão sempre o âmbar
const METHODS: Record<Method, { label: string; swatch: string }> = {
  pix: { label: "PIX", swatch: "bg-chart-1" },
  card: { label: "Cartão", swatch: "bg-chart-2" },
};
const ORDER: Method[] = ["pix", "card"];

// arredondar para 0% (ou 100%) esconderia que houve venda pelo outro meio
const share = (part: number, total: number) => {
  const pct = (part / total) * 100;
  if (pct > 0 && pct < 1) return "menos de 1%";
  if (pct > 99 && pct < 100) return "mais de 99%";
  return `${Math.round(pct)}%`;
};

/**
 * Como os clientes pagaram: uma barra fina dividida (o todo de relance) e, embaixo, cada meio com a amostra
 * da cor, o valor, a parte e os pedidos. Os números ficam no texto; a barra não repete nenhum.
 */
export function PaymentSplit({ methods }: { methods: SalesSummary["by_payment_method"] }) {
  const rows = ORDER.map((m) => methods.find((x) => x.method === m)).filter(
    (x) => !!x && x.gross_sales.amount_cents > 0,
  );
  const total = rows.reduce((s, r) => s + r!.gross_sales.amount_cents, 0);

  return (
    <section
      aria-labelledby="meios"
      className="bg-surface border-line space-y-4 rounded-lg border p-5 sm:p-6"
    >
      <h2 id="meios" className="text-lg font-semibold">
        Como pagaram
      </h2>
      {total === 0 ? (
        <p className="text-ink-soft">Nenhum pedido pago no período.</p>
      ) : (
        <>
          <div aria-hidden="true" className="flex h-2 gap-0.5">
            {rows.map((r) => (
              <span
                key={r!.method}
                className={cn("h-full rounded-full", METHODS[r!.method].swatch)}
                style={{ width: `max(4px, ${(r!.gross_sales.amount_cents / total) * 100}%)` }}
              />
            ))}
          </div>
          <ul className="space-y-3">
            {rows.map((r) => (
              <li key={r!.method} className="flex items-baseline justify-between gap-4">
                <span className="flex items-baseline gap-2">
                  <span
                    aria-hidden="true"
                    className={cn("size-2.5 shrink-0 rounded-sm", METHODS[r!.method].swatch)}
                  />
                  <span>
                    <span className="block font-medium">{METHODS[r!.method].label}</span>
                    <span className="text-ink-soft block text-sm">
                      {r!.paid_orders} {r!.paid_orders === 1 ? "pedido" : "pedidos"}
                    </span>
                  </span>
                </span>
                <span className="text-right">
                  <span className="block tabular-nums">{formatMoney(r!.gross_sales)}</span>
                  <span className="text-ink-soft block text-sm tabular-nums">
                    {share(r!.gross_sales.amount_cents, total)} das vendas
                  </span>
                </span>
              </li>
            ))}
          </ul>
        </>
      )}
    </section>
  );
}
