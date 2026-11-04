import type { Metadata } from "next";
import Link from "next/link";
import { DailyChart } from "@/components/admin/overview/daily-chart";
import { PaymentSplit } from "@/components/admin/overview/payment-split";
import { Receipt } from "@/components/admin/overview/receipt";
import { TopProducts } from "@/components/admin/overview/top-products";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { orderMargins, salesSummary } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { dayOf } from "@/lib/margin-period";
import { buckets, overviewPeriod, overviewPresets, receipt, type OverviewPeriod } from "@/lib/sales-overview";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Visão geral" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };
const one = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);

const DAY = 24 * 60 * 60 * 1000;
const long = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  year: "numeric",
  timeZone: "UTC",
});
const fmtDay = (iso: string) => long.format(new Date(`${iso}T00:00:00Z`));

/**
 * A visão geral das vendas (#382): o extrato do período (do bruto até o que sobrou), as vendas por dia, como os
 * clientes pagaram e os mais vendidos. O período vale para tudo na tela, e fica na URL.
 */
export default async function Overview({ searchParams }: Props) {
  const { token } = await requireAdmin("/admin");
  const query = await searchParams;
  const period = overviewPeriod({ de: one(query.de), ate: one(query.ate) });
  const [sales, margins] = await Promise.all([
    salesSummary(token, { from: period.from, to: period.to }),
    orderMargins(token, { from: period.from, to: period.to }),
  ]);
  const shownFrom = dayOf(Date.parse(period.from));
  const shownTo = dayOf(Date.parse(period.to) - DAY);

  return (
    <div className="@container space-y-6">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Visão geral</h1>
          <p className="text-ink-soft">
            De {fmtDay(shownFrom)} a {fmtDay(shownTo)}, pelos dias de Brasília.
          </p>
        </div>
        <PeriodPicker period={period} />
      </div>

      {!sales.ok ? (
        <StateMessage
          tone="error"
          title="As vendas não carregaram"
          description={`O serviço de pedidos não respondeu (${sales.error.status}). Tente de novo em instantes.`}
          action={
            <Link href="/admin" className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      ) : (
        (() => {
          const s = sales.data;
          const { lines, left } = receipt(s, margins.ok ? margins.data.totals : null);
          const series = buckets(s.daily);
          return (
            <>
              <div className="grid grid-cols-1 gap-6 @4xl:grid-cols-[minmax(20rem,26rem)_minmax(0,1fr)]">
                <Receipt lines={lines} left={left} partial={!margins.ok} />
                <div className="space-y-6">
                  <dl className="grid grid-cols-2 gap-3 @2xl:grid-cols-4">
                    {[
                      ["Pedidos pagos", String(s.orders.paid)],
                      ["Ticket médio", formatMoney(s.average_ticket)],
                      ["Estornados", String(s.orders.refunded)],
                      ["Não pagos", String(s.orders.not_paid)],
                    ].map(([label, value]) => (
                      <div key={label} className="bg-surface border-line rounded-lg border px-4 py-3">
                        <dt className="text-ink-soft text-sm">{label}</dt>
                        <dd className="text-xl font-semibold">{value}</dd>
                      </div>
                    ))}
                  </dl>
                  <section
                    aria-labelledby="por-dia"
                    className="bg-surface border-line space-y-4 rounded-lg border p-5 sm:p-6"
                  >
                    <h2 id="por-dia" className="text-lg font-semibold">
                      Vendas brutas por {series.unit}
                    </h2>
                    {s.orders.paid === 0 ? (
                      <p className="text-ink-soft">Nenhum pedido pago no período.</p>
                    ) : (
                      <DailyChart items={series.items} unit={series.unit} />
                    )}
                  </section>
                </div>
              </div>
              <div className="grid grid-cols-1 gap-6 @3xl:grid-cols-2">
                <PaymentSplit methods={s.by_payment_method} />
                <TopProducts products={s.top_products} />
              </div>
            </>
          );
        })()
      )}
    </div>
  );
}

/**
 * O período: os atalhos numa fileira (o que quase sempre se quer) e o intervalo exato num formulário GET
 * embaixo de "Outro período". O atalho em uso fica marcado; o formulário avisa o período que não vale.
 */
function PeriodPicker({ period }: { period: OverviewPeriod }) {
  const presets = overviewPresets();
  const isPreset = presets.some((p) => p.de === period.de && p.ate === period.ate);
  const field =
    "bg-surface text-ink border-field-line aria-invalid:border-danger h-11 rounded-md border px-3 text-base";
  return (
    <div className="space-y-2">
      <nav aria-label="Período" className="bg-ink/6 inline-flex flex-wrap gap-1 rounded-lg p-1">
        {presets.map((p) => {
          const current = p.de === period.de && p.ate === period.ate && !period.error;
          return (
            <Link
              key={p.label}
              href={`/admin?de=${p.de}&ate=${p.ate}`}
              aria-current={current ? "page" : undefined}
              className={cn(
                "inline-flex min-h-11 items-center rounded-md px-3 text-sm font-medium pointer-fine:min-h-9",
                current ? "bg-surface text-ink shadow-sm" : "text-ink-soft hover:text-ink",
              )}
            >
              {p.label}
            </Link>
          );
        })}
      </nav>
      <details open={!isPreset || !!period.error} className="text-sm">
        <summary className="inline-flex min-h-11 cursor-pointer items-center font-medium underline-offset-4 hover:underline pointer-fine:min-h-8">
          Outro período
        </summary>
        <form method="get" action="/admin" className="mt-2 flex flex-wrap items-end gap-2">
          <label className="space-y-1">
            <span className="block font-medium">De</span>
            <input
              type="date"
              name="de"
              defaultValue={period.de}
              aria-invalid={period.error ? true : undefined}
              aria-describedby={period.error ? "periodo-erro" : undefined}
              className={field}
            />
          </label>
          <label className="space-y-1">
            <span className="block font-medium">Até</span>
            <input
              type="date"
              name="ate"
              defaultValue={period.ate}
              aria-invalid={period.error ? true : undefined}
              aria-describedby={period.error ? "periodo-erro" : undefined}
              className={field}
            />
          </label>
          <button type="submit" className={buttonVariants({ variant: "secondary" })}>
            Ver o período
          </button>
        </form>
        {period.error && (
          <p id="periodo-erro" role="alert" className="text-danger mt-2 font-medium">
            {period.error} A tela mostra os últimos 30 dias.
          </p>
        )}
      </details>
    </div>
  );
}
