import type { Metadata } from "next";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { orderMargins, type OrderMargin, type OrderMarginTotals } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { marginPeriod, presets, type MarginPeriod } from "@/lib/margin-period";
import { orderNumber } from "@/lib/order-status";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Margens" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };
const one = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  timeZone: "America/Sao_Paulo",
});
const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });
const money = (cents: number | null | undefined) => (cents == null ? "—" : brl(cents));

const OUTCOME: Record<OrderMargin["outcome"], string> = {
  in_progress: "Em andamento",
  delivered: "Entregue",
  cancelled: "Cancelado",
  returned: "Devolvido",
};

/** Por que a margem não sai: o pedido estornado não entra na conta, e o resto é um valor que falta. */
function noMargin(o: OrderMargin): string {
  if (o.outcome === "cancelled" || o.outcome === "returned") return "Estornado";
  if (o.product_cost_cents == null) return "Sem custo";
  return "Sem taxa";
}

/**
 * A margem real por pedido (#225): o que o cliente pagou, menos o custo dos produtos no câmbio da compra, o frete
 * repassado ao fornecedor e a taxa do pagamento. O período (#282) filtra a lista e a soma; a soma vem da API e
 * cobre o período inteiro, mesmo quando a lista mostra só os 200 mais recentes.
 */
export default async function Margins({ searchParams }: Props) {
  const { token } = await requireAdmin("/admin/margens");
  const query = await searchParams;
  const period = marginPeriod({ de: one(query.de), ate: one(query.ate) });
  const result = await orderMargins(token, period.error ? {} : { from: period.from, to: period.to });

  return (
    <div className="max-w-5xl space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Margens</h1>
        <p className="text-ink-soft max-w-prose">
          O que sobra de cada pedido depois do custo dos produtos, do frete repassado ao fornecedor e da taxa
          do pagamento. A lista mostra até 200 pedidos do período, e a soma cobre o período inteiro.
        </p>
      </div>

      <PeriodForm period={period} />

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="As margens não carregaram"
          description={`O serviço do fornecedor não respondeu (${result.error.status}). Tente de novo em instantes.`}
          action={
            <Link href="/admin/margens" className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      ) : result.data.data.length === 0 ? (
        <StateMessage
          title={period.from || period.to ? "Nenhum pedido no período" : "Nenhum pedido ainda"}
          description={
            period.from || period.to
              ? "Escolha outro período, ou veja todos os pedidos."
              : "A margem aparece aqui quando um pedido pago vai ao fornecedor."
          }
        />
      ) : (
        <>
          <Totals totals={result.data.totals} />
          <MarginTable orders={result.data.data} />
        </>
      )}
    </div>
  );
}

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/** O filtro do período: um formulário GET (funciona sem JavaScript), com atalhos para os períodos comuns. */
function PeriodForm({ period }: { period: MarginPeriod }) {
  const field =
    "bg-surface text-ink border-field-line aria-invalid:border-danger h-11 rounded-md border px-3 text-base";
  return (
    <form method="get" action="/admin/margens" aria-labelledby="periodo" className="space-y-3">
      <h2 id="periodo" className="font-semibold">
        Período
      </h2>
      <div className="flex flex-wrap items-end gap-3">
        <label className="space-y-1.5">
          <span className="block text-sm font-medium">De</span>
          <input
            type="date"
            name="de"
            defaultValue={period.de}
            aria-invalid={period.error ? true : undefined}
            aria-describedby={period.error ? "periodo-erro" : undefined}
            className={field}
          />
        </label>
        <label className="space-y-1.5">
          <span className="block text-sm font-medium">Até</span>
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
      </div>
      {period.error && (
        <p id="periodo-erro" role="alert" className="text-danger text-sm font-medium">
          {period.error} A tela mostra todos os pedidos.
        </p>
      )}
      <ul className="flex flex-wrap gap-x-4 text-sm" aria-label="Atalhos de período">
        {presets().map((p) => (
          <li key={p.label}>
            <Link
              href={`/admin/margens?de=${p.de}&ate=${p.ate}`}
              aria-current={p.de === period.de && p.ate === period.ate ? "page" : undefined}
              className="inline-flex min-h-11 items-center font-medium underline-offset-4 hover:underline aria-[current=page]:underline pointer-fine:min-h-8"
            >
              {p.label}
            </Link>
          </li>
        ))}
        <li>
          <Link
            href="/admin/margens"
            aria-current={!period.de && !period.ate ? "page" : undefined}
            className="inline-flex min-h-11 items-center font-medium underline-offset-4 hover:underline aria-[current=page]:underline pointer-fine:min-h-8"
          >
            Todos os pedidos
          </Link>
        </li>
      </ul>
    </form>
  );
}

/** A soma do período, que vem da API: só os pedidos com a conta completa, e o que ficou fora dela. */
function Totals({ totals: t }: { totals: OrderMarginTotals }) {
  const left = [
    t.refunded > 0 && plural(t.refunded, "estornado", "estornados"),
    t.incomplete > 0 && `${plural(t.incomplete, "pedido", "pedidos")} sem algum valor`,
  ].filter(Boolean);
  return (
    <section aria-labelledby="soma" className="space-y-3">
      <h2 id="soma" className="sr-only">
        Soma do período
      </h2>
      {t.counted === 0 ? (
        <p className="text-ink-soft">Nenhum pedido do período tem a conta completa para somar.</p>
      ) : (
        <>
          <p>
            <span
              className={cn(
                "font-condensed text-3xl font-extrabold tabular-nums",
                t.margin_cents < 0 && "text-danger",
              )}
            >
              {brl(t.margin_cents)}
            </span>{" "}
            de margem em {plural(t.counted, "pedido", "pedidos")} com a conta completa
            {t.revenue_cents > 0 && (
              <>, {Math.round((t.margin_cents / t.revenue_cents) * 100)}% do que os clientes pagaram</>
            )}
            .
          </p>
          <dl className="grid grid-cols-2 gap-x-6 gap-y-1 text-sm sm:grid-cols-4">
            {[
              ["Pago", t.revenue_cents],
              ["Custo", t.product_cost_cents],
              ["Frete", t.freight_cents],
              ["Taxa", t.fee_cents],
            ].map(([label, cents]) => (
              <div key={label}>
                <dt className="text-ink-soft">{label}</dt>
                <dd className="tabular-nums">{brl(cents as number)}</dd>
              </div>
            ))}
          </dl>
        </>
      )}
      {left.length > 0 && (
        <p className="text-ink-soft text-sm">
          Fora da soma: {left.join(" e ")}. Estornado não entra: o quanto a loja perde nele depende do que o
          fornecedor devolver.
        </p>
      )}
    </section>
  );
}

function MarginTable({ orders }: { orders: OrderMargin[] }) {
  return (
    <>
      {/* a tabela rola sozinha no celular: tabIndex para rolar também pelo teclado */}
      <div
        role="region"
        aria-label="Margem por pedido"
        tabIndex={0}
        className="border-line overflow-x-auto rounded-md border focus-visible:outline-2"
      >
        <table className="w-full min-w-3xl text-sm">
          <thead className="border-line border-b text-left">
            <tr>
              <th scope="col" className="px-4 py-2 font-semibold">
                Pedido
              </th>
              <th scope="col" className="px-4 py-2 font-semibold">
                Situação
              </th>
              {["Pago", "Custo", "Frete", "Taxa", "Margem"].map((h) => (
                <th key={h} scope="col" className="px-4 py-2 text-right font-semibold">
                  {h}
                </th>
              ))}
            </tr>
          </thead>
          <tbody className="divide-line divide-y">
            {orders.map((o) => (
              <tr key={o.order_id}>
                <th scope="row" className="px-4 py-2 text-left font-semibold whitespace-nowrap">
                  {orderNumber(o.order_id)}{" "}
                  <time dateTime={o.created_at} className="text-ink-soft font-normal">
                    {when.format(new Date(o.created_at))}
                  </time>
                </th>
                <td className="px-4 py-2 whitespace-nowrap">{OUTCOME[o.outcome]}</td>
                {[o.revenue_cents, o.product_cost_cents, o.freight_cents, o.fee_cents].map((v, i) => (
                  <td key={i} className="px-4 py-2 text-right whitespace-nowrap tabular-nums">
                    {money(v)}
                  </td>
                ))}
                <td
                  className={cn(
                    "px-4 py-2 text-right font-semibold whitespace-nowrap tabular-nums",
                    o.margin_cents == null && "text-ink-soft font-normal",
                    o.margin_cents != null && o.margin_cents < 0 && "text-danger",
                  )}
                >
                  {o.margin_cents == null ? noMargin(o) : brl(o.margin_cents)}
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    </>
  );
}
