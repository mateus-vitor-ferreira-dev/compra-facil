import Link from "next/link";
import { Badge } from "@/components/ui/badge";
import type { AdminOrderSummary } from "@/lib/admin-api";
import { adminOrderStatus } from "@/lib/admin-orders";
import { formatPhone } from "@/lib/delivery";
import { formatMoney } from "@/lib/format";
import { orderNumber } from "@/lib/order-status";
import { cn } from "@/lib/utils";

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});

export const paymentLabel = (p: AdminOrderSummary["payment"]) =>
  p.method === "pix" ? "PIX" : p.installments > 1 ? `Cartão em ${p.installments}x` : "Cartão à vista";

/*
 * As colunas mudam com a largura da própria lista (container query), não da tela: a mesma lista cabe sozinha
 * numa tela grande (com telefone e destino), ao lado do detalhe (sem eles) e no celular (em duas linhas).
 */
const row =
  "grid grid-cols-[minmax(0,1fr)_auto] gap-x-4 gap-y-1 px-4 py-3 @xl:grid-cols-[7.5rem_minmax(0,1fr)_7.5rem_12.5rem] @xl:items-center @4xl:grid-cols-[7.5rem_minmax(0,1.4fr)_9rem_minmax(0,1fr)_7.5rem_12.5rem]";

/**
 * Os pedidos da equipe (#382): número e quando, quem comprou (nome e e-mail), telefone e destino quando há
 * espaço, total com o meio de pagamento e a situação. Cada linha leva ao detalhe; a aberta fica marcada.
 */
export function OrderList({
  orders,
  hrefFor,
  selectedId,
}: {
  orders: AdminOrderSummary[];
  hrefFor: (id: string) => string;
  selectedId?: string;
}) {
  return (
    <div className="@container">
      <div className="bg-surface border-line overflow-hidden rounded-lg border">
        {/* os nomes das colunas, só quando a linha é uma só (nas duas linhas, cada valor se explica) */}
        <div
          aria-hidden="true"
          className={cn(row, "text-ink-soft border-line hidden border-b py-2.5 text-sm font-medium @xl:grid")}
        >
          <span>Pedido</span>
          <span>Cliente</span>
          <span className="hidden @4xl:block">Telefone</span>
          <span className="hidden @4xl:block">Destino</span>
          <span className="text-right">Total</span>
          <span>Situação</span>
        </div>
        <ul>
          {orders.map((o) => {
            const status = adminOrderStatus(o);
            const selected = o.id === selectedId;
            return (
              <li key={o.id} className="border-line border-b last:border-b-0">
                <Link
                  href={hrefFor(o.id)}
                  aria-current={selected ? "page" : undefined}
                  className={cn(
                    row,
                    "hover:bg-ink/4 relative focus-visible:-outline-offset-2",
                    selected &&
                      "bg-ink/6 before:bg-ink before:absolute before:inset-y-0 before:left-0 before:w-1",
                  )}
                >
                  <span className="min-w-0">
                    <span className="block font-semibold tabular-nums">{orderNumber(o.id)}</span>
                    <span className="text-ink-soft block text-sm">{when.format(new Date(o.created_at))}</span>
                  </span>

                  {/* nas duas linhas, o total sobe para o lado do número */}
                  <span className="text-right @xl:order-last @xl:hidden">
                    <Total order={o} />
                  </span>

                  <span className="min-w-0">
                    <span className="block truncate font-medium">{o.customer.name ?? "Sem nome"}</span>
                    <span className="text-ink-soft block truncate text-sm">{o.customer.email}</span>
                  </span>

                  <span className="text-ink-soft hidden text-sm tabular-nums @4xl:block">
                    {o.customer.phone ? formatPhone(o.customer.phone) : "—"}
                  </span>
                  <span className="text-ink-soft hidden truncate text-sm @4xl:block">
                    {o.destination ? `${o.destination.city}/${o.destination.state}` : "—"}
                  </span>

                  <span className="hidden text-right @xl:block">
                    <Total order={o} />
                  </span>

                  <span className="flex items-center justify-end @xl:justify-start">
                    <Badge variant={status.tone} className="whitespace-nowrap">
                      {status.label}
                    </Badge>
                  </span>
                </Link>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}

function Total({ order }: { order: AdminOrderSummary }) {
  return (
    <>
      <span className="font-condensed block text-lg leading-tight font-bold tabular-nums">
        {formatMoney(order.total)}
      </span>
      <span className="text-ink-soft block text-sm">{paymentLabel(order.payment)}</span>
    </>
  );
}
