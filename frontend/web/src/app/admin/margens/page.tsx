import type { Metadata } from "next";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { orderMargins, type OrderMargin } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { orderNumber } from "@/lib/order-status";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Margens" };

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
 * repassado ao fornecedor e a taxa do pagamento. A soma só conta os pedidos com a conta completa, e diz quantos.
 */
export default async function Margins() {
  const { token } = await requireAdmin("/admin/margens");
  const result = await orderMargins(token);

  return (
    <div className="max-w-5xl space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Margens</h1>
        <p className="text-ink-soft max-w-prose">
          O que sobra de cada pedido depois do custo dos produtos, do frete repassado ao fornecedor e da taxa
          do pagamento. Mostra os 200 pedidos mais recentes.
        </p>
      </div>

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
          title="Nenhum pedido ainda"
          description="A margem aparece aqui quando um pedido pago vai ao fornecedor."
        />
      ) : (
        <MarginTable orders={result.data.data} />
      )}
    </div>
  );
}

function MarginTable({ orders }: { orders: OrderMargin[] }) {
  const counted = orders.filter((o) => o.margin_cents != null);
  const total = counted.reduce((sum, o) => sum + o.margin_cents!, 0);
  const revenue = counted.reduce((sum, o) => sum + o.revenue_cents!, 0);
  return (
    <>
      {counted.length > 0 && (
        <p>
          <span
            className={cn("font-condensed text-3xl font-extrabold tabular-nums", total < 0 && "text-danger")}
          >
            {brl(total)}
          </span>{" "}
          de margem em {counted.length === 1 ? "1 pedido" : `${counted.length} pedidos`} com a conta completa
          {revenue > 0 && <>, {Math.round((total / revenue) * 100)}% do que os clientes pagaram</>}.
        </p>
      )}
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
