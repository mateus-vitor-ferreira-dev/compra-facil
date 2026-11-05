import { Mail, MessageCircle, Phone } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import type { AdminOrderDetail } from "@/lib/admin-api";
import { adminOrderStatus, whatsappHref } from "@/lib/admin-orders";
import { formatPhone } from "@/lib/delivery";
import { formatMoney } from "@/lib/format";
import { orderNumber, trackingHref } from "@/lib/order-status";
import { paymentLabel } from "./order-list";

const at = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  year: "numeric",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});
const fmt = (iso: string) => at.format(new Date(iso));

const contactLink =
  "text-ink hover:bg-ink/6 -mx-2 inline-flex min-h-11 max-w-full items-center gap-2 rounded-md px-2 font-medium underline-offset-4 hover:underline pointer-fine:min-h-9";

/** As etapas que o pedido viveu, com o horário de cada uma; as que não aconteceram ficam de fora. */
function timeline(o: AdminOrderDetail): { label: string; at: string }[] {
  const steps: [string, string | null | undefined][] = [
    ["Pedido feito", o.created_at],
    ["Estoque reservado", o.stock_reserved_at],
    ["Aceito pelo fornecedor", o.sent_to_supplier_at],
    ["Enviado", o.shipped_at],
    ["Entregue", o.delivered_at],
    ["Devolução pedida", o.return_requested_at],
    ["Valor estornado", o.refunded_at],
  ];
  return steps.filter((s): s is [string, string] => !!s[1]).map(([label, when]) => ({ label, at: when }));
}

/**
 * O pedido para a equipe (#382): quem comprou e como falar com ela, o que comprou e quanto pagou, a entrega e
 * o que aconteceu até aqui. Sem CPF nem endereço: a API do painel não os manda.
 */
export function OrderDetail({ order: o }: { order: AdminOrderDetail }) {
  const status = adminOrderStatus(o);
  const wa = whatsappHref(o.customer.phone);
  const track = o.tracking ? trackingHref(o.tracking.url) : null;

  return (
    <article aria-labelledby="pedido-titulo" className="space-y-8">
      <header className="space-y-2">
        <div className="flex flex-wrap items-center gap-3">
          <h1 id="pedido-titulo" className="text-2xl font-semibold tracking-tight">
            Pedido <span className="tabular-nums">{orderNumber(o.id)}</span>
          </h1>
          <Badge variant={status.tone}>{status.label}</Badge>
        </div>
        <p className="text-ink-soft">Feito em {fmt(o.created_at)}.</p>
      </header>

      <div className="grid gap-8 @3xl:grid-cols-2">
        <section aria-labelledby="cliente" className="min-w-0 space-y-3">
          <h2 id="cliente" className="font-semibold">
            Cliente
          </h2>
          <div className="space-y-1">
            <p className="text-lg font-medium">{o.customer.name ?? "Sem nome no pedido"}</p>
            {o.destination && (
              <p className="text-ink-soft">
                Entrega em {o.destination.city}/{o.destination.state}
              </p>
            )}
          </div>
          <ul className="space-y-0.5">
            <li>
              <a href={`mailto:${o.customer.email}`} className={contactLink}>
                <Mail aria-hidden="true" className="text-ink-soft size-4 shrink-0" />
                <span className="min-w-0 truncate">{o.customer.email}</span>
              </a>
            </li>
            {o.customer.phone && (
              <li>
                <a href={`tel:+55${o.customer.phone}`} className={contactLink}>
                  <Phone aria-hidden="true" className="text-ink-soft size-4" />
                  <span className="tabular-nums">{formatPhone(o.customer.phone)}</span>
                </a>
              </li>
            )}
            {wa && (
              <li>
                <a href={wa} target="_blank" rel="noopener noreferrer" className={contactLink}>
                  <MessageCircle aria-hidden="true" className="text-ink-soft size-4" />
                  Conversar no WhatsApp
                  <span className="sr-only"> (abre em outra aba)</span>
                </a>
              </li>
            )}
          </ul>
        </section>

        <section aria-labelledby="etapas" className="space-y-3">
          <h2 id="etapas" className="font-semibold">
            O que aconteceu
          </h2>
          <ol className="border-line space-y-3 border-l-2 pl-4">
            {timeline(o).map((s) => (
              <li key={s.label} className="relative">
                <span
                  aria-hidden="true"
                  className="bg-ink absolute top-2 -left-[1.3rem] size-2 rounded-full"
                />
                <span className="block font-medium">{s.label}</span>
                <time dateTime={s.at} className="text-ink-soft block text-sm">
                  {fmt(s.at)}
                </time>
              </li>
            ))}
          </ol>
        </section>
      </div>

      <section aria-labelledby="itens" className="space-y-3">
        <h2 id="itens" className="font-semibold">
          Itens
        </h2>
        <div className="bg-surface border-line rounded-lg border">
          <ul>
            {o.items.map((i) => (
              <li
                key={`${i.product_id}-${i.sku}`}
                className="border-line flex items-baseline justify-between gap-4 border-b px-4 py-3"
              >
                <span className="min-w-0">
                  <span className="block font-medium">{i.name}</span>
                  <span className="text-ink-soft block text-sm break-all">
                    {i.quantity} × {formatMoney(i.unit_price)}, SKU {i.sku}
                  </span>
                </span>
                <span className="tabular-nums">{formatMoney(i.line_total)}</span>
              </li>
            ))}
          </ul>
          <dl className="space-y-1 px-4 py-3 text-sm">
            <div className="flex justify-between gap-4">
              <dt className="text-ink-soft">Produtos</dt>
              <dd className="tabular-nums">{formatMoney(o.items_total)}</dd>
            </div>
            {o.shipping && (
              <div className="flex justify-between gap-4">
                <dt className="text-ink-soft">Frete {o.shipping.name.toLowerCase()}</dt>
                <dd className="tabular-nums">{formatMoney(o.shipping.price)}</dd>
              </div>
            )}
            <div className="flex items-baseline justify-between gap-4 pt-1">
              <dt className="font-semibold">Total, {paymentLabel(o.payment)}</dt>
              <dd className="font-condensed text-xl font-bold tabular-nums">{formatMoney(o.total)}</dd>
            </div>
          </dl>
        </div>
      </section>

      {(o.shipping || o.tracking || o.invoice_number) && (
        <section aria-labelledby="entrega" className="space-y-2">
          <h2 id="entrega" className="font-semibold">
            Entrega e nota
          </h2>
          <ul className="text-ink-soft space-y-1">
            {o.shipping && (
              <li>
                Prometido para {new Date(`${o.shipping.promised_by}T12:00:00`).toLocaleDateString("pt-BR")}
              </li>
            )}
            {o.tracking && (
              <li>
                Rastreio{" "}
                {track ? (
                  <a
                    href={track}
                    target="_blank"
                    rel="noopener noreferrer"
                    className="text-ink font-medium underline underline-offset-4"
                  >
                    {o.tracking.number}
                    <span className="sr-only"> (abre em outra aba)</span>
                  </a>
                ) : (
                  <span className="text-ink font-medium">{o.tracking.number}</span>
                )}
                {o.tracking.provider && ` (${o.tracking.provider})`}
              </li>
            )}
            {o.invoice_number && <li>Nota fiscal número {o.invoice_number}</li>}
          </ul>
        </section>
      )}
    </article>
  );
}
