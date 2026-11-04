import Link from "next/link";
import type { SalesSummary } from "@/lib/admin-api";
import { formatMoney } from "@/lib/format";

/**
 * Os mais vendidos do período, pelo que faturaram (os estornados não contam): barras numa cor só, do maior
 * para o menor, no mesmo chão. O valor vai na ponta de cada barra, em texto, e o nome leva ao produto no painel.
 */
export function TopProducts({ products }: { products: SalesSummary["top_products"] }) {
  const max = Math.max(...products.map((p) => p.revenue.amount_cents), 1);
  return (
    <section
      aria-labelledby="mais-vendidos"
      className="bg-surface border-line space-y-4 rounded-lg border p-5 sm:p-6"
    >
      <h2 id="mais-vendidos" className="text-lg font-semibold">
        Mais vendidos
      </h2>
      {products.length === 0 ? (
        <p className="text-ink-soft">Nenhum produto vendido no período.</p>
      ) : (
        <ol className="space-y-3">
          {products.map((p) => (
            <li key={p.product_id} className="space-y-1">
              <div className="flex items-baseline justify-between gap-4">
                <Link
                  href={`/admin/produtos/${p.product_id}`}
                  className="min-w-0 truncate font-medium underline-offset-4 hover:underline"
                >
                  {p.name}
                </Link>
                <span className="text-ink-soft shrink-0 text-sm tabular-nums">
                  {p.quantity} {p.quantity === 1 ? "unidade" : "unidades"}
                </span>
              </div>
              <div className="flex items-center gap-2">
                <span
                  aria-hidden="true"
                  className="bg-chart-1 h-3 rounded-r-[4px]"
                  style={{ width: `max(2px, calc((100% - 8rem) * ${p.revenue.amount_cents / max}))` }}
                />
                <span className="text-sm whitespace-nowrap tabular-nums">{formatMoney(p.revenue)}</span>
              </div>
            </li>
          ))}
        </ol>
      )}
    </section>
  );
}
