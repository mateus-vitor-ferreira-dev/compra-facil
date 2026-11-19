import type { Metadata } from "next";
import Link from "next/link";
import { PlaceOrderForm, TrackingForm } from "@/components/admin/manual-order-forms";
import { buttonVariants } from "@/components/ui/button";
import { CopyButton } from "@/components/ui/copy-button";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { manualOrders, ordersToPlace, supplierSheet, type ManualOrder } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { orderNumber } from "@/lib/order-status";
import { cn } from "@/lib/utils";
import { cancelManualOrder, deliverOrder, outOfStock } from "./actions";

export const metadata: Metadata = { title: "Pedidos a fazer" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });
const when = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "America/Sao_Paulo",
});
const day = new Intl.DateTimeFormat("pt-BR", { day: "2-digit", month: "2-digit", timeZone: "UTC" });

const ERRORS: Record<string, string> = {
  "sem-estoque": "O pedido não foi marcado sem estoque",
  entregue: "A entrega não foi registrada",
  cancelar: "O pedido não foi cancelado",
};

/** O que a página conta depois de uma ação (a query que a ação deixou). */
function notice(q: Record<string, string | string[] | undefined>): { ok: boolean; text: string } | undefined {
  const n = (k: string) => orderNumber(String(q[k]));
  if (typeof q.feito === "string")
    return { ok: true, text: `Pedido ${n("feito")} registrado. O cliente vê que ele foi para o fornecedor.` };
  if (typeof q.rastreio === "string")
    return { ok: true, text: `Rastreio do pedido ${n("rastreio")} enviado ao cliente.` };
  if (typeof q.entregue === "string")
    return { ok: true, text: `Pedido ${n("entregue")} marcado como entregue.` };
  if (typeof q["sem-estoque"] === "string")
    return { ok: true, text: `Pedido ${n("sem-estoque")} sem estoque: o cliente é estornado por inteiro.` };
  if (typeof q.cancelado === "string")
    return { ok: true, text: `Pedido ${n("cancelado")} cancelado: o cliente é estornado por inteiro.` };
  if (typeof q.erro === "string") {
    const status = String(q.status ?? "");
    return {
      ok: false,
      text:
        q.erro === "cancelar" && status === "409"
          ? "O pedido já foi feito no fornecedor: cancele lá primeiro e marque a confirmação."
          : `${ERRORS[q.erro] ?? "A ação não foi feita"} (${status}). Atualize a página e tente de novo.`,
    };
  }
}

/** Há quanto tempo o pedido está pago e esperando, e o tom: cada hora parada sai do prazo prometido ao cliente. */
function waiting(since: string, now: number): { text: string; tone: "ok" | "warn" | "late" } {
  const minutes = Math.max(1, Math.floor((now - Date.parse(since)) / 60_000));
  const hours = minutes / 60;
  const text =
    minutes < 60
      ? `${minutes} min`
      : hours < 48
        ? `${Math.floor(hours)} h`
        : `${Math.floor(hours / 24)} dias`;
  return { text, tone: hours >= 24 ? "late" : hours >= 4 ? "warn" : "ok" };
}

/**
 * O fornecedor manual (#412): a equipe faz cada pedido no site do fornecedor. Primeiro a fila, os mais antigos em
 * cima, com os dados na ordem em que o formulário do fornecedor pede; depois os que esperam o rastreio e os em
 * transporte.
 */
export default async function OrdersToPlace({ searchParams }: Props) {
  const { token } = await requireAdmin("/admin/pedidos-a-fazer");
  const [sheet, { result: queue, readAt: now }, placed, shipped, query] = await Promise.all([
    supplierSheet(token),
    ordersToPlace(token),
    manualOrders(token, "placed"),
    manualOrders(token, "shipped"),
    searchParams,
  ]);
  const message = notice(query);

  return (
    <div className="max-w-5xl space-y-8">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Pedidos a fazer</h1>
        <p className="text-ink-soft max-w-prose">
          Pedidos pagos que a equipe faz no site do fornecedor, os mais antigos primeiro. Depois de feito, o
          pedido espera aqui o rastreio e a entrega.
        </p>
      </div>

      <div aria-live="polite">
        {message && (
          <p
            role={message.ok ? "status" : "alert"}
            className={message.ok ? "text-success font-medium" : "text-danger font-medium"}
          >
            {message.text}
          </p>
        )}
      </div>

      {sheet.result.ok && !sheet.result.data.enabled ? (
        <StateMessage
          title="O fornecedor não está no modo manual"
          description="Os pedidos vão ao fornecedor pela API dele. Os parados aparecem em Pedidos no fornecedor."
          action={
            <Link href="/admin/atrasos" className={buttonVariants({ variant: "secondary" })}>
              Ver os pedidos no fornecedor
            </Link>
          }
        />
      ) : !queue.ok ? (
        <StateMessage
          tone="error"
          title="A fila não carregou"
          description={`O serviço do fornecedor não respondeu (${queue.error.status}). Tente de novo em instantes.`}
          action={
            <Link href="/admin/pedidos-a-fazer" className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      ) : (
        <>
          <section aria-labelledby="fila" className="space-y-3">
            <Heading id="fila" title="A fazer no fornecedor" count={queue.data.data.length} />
            {queue.data.data.length === 0 ? (
              <p className="text-ink-soft">
                Nenhum pedido esperando. Os novos aparecem aqui assim que o pagamento é aprovado.
              </p>
            ) : (
              <ol className="space-y-4">
                {queue.data.data.map((o) => (
                  <Ticket key={o.order_id} order={o} now={now} />
                ))}
              </ol>
            )}
          </section>

          <Stage
            id="rastreio"
            title="Esperando o rastreio"
            description="Feitos no fornecedor. Informe o código assim que ele despachar: o cliente recebe na hora."
            result={placed}
          >
            {(o) => (
              <>
                <OrderLine order={o} />
                <TrackingForm orderId={o.order_id} number={orderNumber(o.order_id)} />
                <CancelPlaced order={o} />
              </>
            )}
          </Stage>

          <Stage
            id="transporte"
            title="Em transporte"
            description="Marque a entrega quando o rastreio mostrar. Antes disso, o cliente vê o pedido a caminho."
            result={shipped}
          >
            {(o) => (
              <div className="flex flex-wrap items-center justify-between gap-3">
                <OrderLine order={o} />
                <form action={deliverOrder}>
                  <input type="hidden" name="order_id" value={o.order_id} />
                  <SubmitButton variant="secondary" size="sm" pendingLabel="Registrando…">
                    Marcar como entregue<span className="sr-only"> o pedido {orderNumber(o.order_id)}</span>
                  </SubmitButton>
                </form>
              </div>
            )}
          </Stage>
        </>
      )}
    </div>
  );
}

function Heading({ id, title, count }: { id: string; title: string; count: number }) {
  return (
    <h2 id={id} className="text-xl font-semibold tracking-tight">
      {title}
      <span className="font-condensed ml-2 tabular-nums">
        <span className="sr-only">: </span>
        {count}
        <span className="sr-only"> {count === 1 ? "pedido" : "pedidos"}</span>
      </span>
    </h2>
  );
}

function Stage({
  id,
  title,
  description,
  result,
  children,
}: {
  id: string;
  title: string;
  description: string;
  result: Awaited<ReturnType<typeof manualOrders>>;
  children: (o: ManualOrder) => React.ReactNode;
}) {
  if (!result.ok)
    return (
      <p className="text-danger font-medium">
        {title}: não carregou ({result.error.status}). Atualize a página.
      </p>
    );
  if (result.data.data.length === 0) return null;
  return (
    <section aria-labelledby={id} className="space-y-3">
      <div>
        <Heading id={id} title={title} count={result.data.data.length} />
        <p className="text-ink-soft text-sm">{description}</p>
      </div>
      <ul className="divide-line border-line divide-y rounded-md border">
        {result.data.data.map((o) => (
          <li key={o.order_id} className="space-y-3 px-4 py-3 text-sm">
            {children(o)}
          </li>
        ))}
      </ul>
    </section>
  );
}

function OrderLine({ order: o }: { order: ManualOrder }) {
  return (
    <div className="space-y-0.5">
      <p className="font-semibold">
        Pedido {orderNumber(o.order_id)}
        {o.supplier_order_id && (
          <span className="text-ink-soft font-normal">, no fornecedor {o.supplier_order_id}</span>
        )}
      </p>
      <p className="text-ink-soft">
        {o.items.map((i) => `${i.quantity} × ${i.name}${i.size ? ` (${i.size})` : ""}`).join(", ")}
        {o.placed_at && (
          <>
            ; feito em <time dateTime={o.placed_at}>{when.format(new Date(o.placed_at))}</time>
          </>
        )}
        {o.tracking && <>; rastreio {o.tracking.number}</>}
      </p>
    </div>
  );
}

/** A ficha de compra: tudo o que o formulário do fornecedor pede, na ordem, com um botão de copiar em cada dado. */
function Ticket({ order: o, now }: { order: ManualOrder; now: number }) {
  const number = orderNumber(o.order_id);
  const age = waiting(o.awaiting_since, now);
  const s = o.ship_to;
  const address = s
    ? `${s.street}, ${s.number}${s.complement ? `, ${s.complement}` : ""}, ${s.district}, ${s.city} - ${s.state}, ${s.zip_code}`
    : "";
  const expected = (o.expected_product_cost_cents ?? 0) + (o.expected_freight_cost_cents ?? 0);
  return (
    <li
      aria-labelledby={`ficha-${o.order_id}`}
      className={cn(
        "bg-surface rounded-md border-2 p-4 sm:p-5",
        age.tone === "late" ? "border-danger" : age.tone === "warn" ? "border-ink" : "border-line",
      )}
    >
      <div className="flex flex-wrap items-baseline justify-between gap-x-6 gap-y-1">
        <h3 id={`ficha-${o.order_id}`} className="text-lg font-semibold">
          Pedido {number}
        </h3>
        <p className={cn("font-semibold tabular-nums", age.tone === "late" && "text-danger")}>
          Pago há <time dateTime={o.awaiting_since}>{age.text}</time>
          {o.promised_by && (
            <span className="text-ink-soft font-normal">
              , entregar até <time dateTime={o.promised_by}>{day.format(new Date(o.promised_by))}</time>
            </span>
          )}
        </p>
      </div>

      <div className="mt-4 grid gap-6 md:grid-cols-2">
        <div className="space-y-2">
          <h4 className="font-medium">Itens</h4>
          <ul className="space-y-1 text-sm">
            {o.items.map((i) => (
              <li key={`${i.sku}:${i.size ?? ""}`} className="flex items-center justify-between gap-2">
                <span>
                  <span className="font-semibold tabular-nums">{i.quantity} ×</span> {i.name}
                  {/* no modo manual, a equipe pede ao fornecedor nesse tamanho: em destaque */}
                  {i.size && (
                    <span className="border-ink ml-2 rounded border px-1.5 py-0.5 text-xs font-bold">
                      Tamanho {i.size}
                    </span>
                  )}
                  <span className="text-ink-soft block tabular-nums">SKU {i.sku}</span>
                </span>
                <CopyButton value={i.sku} label={`o SKU ${i.sku}`} />
              </li>
            ))}
          </ul>
          <p className="text-ink-soft text-sm">
            {expected > 0 ? <>Custo esperado {brl(expected)}</> : "Sem custo na planilha"}
            {o.revenue_cents != null && <>; o cliente pagou {brl(o.revenue_cents)}</>}
            {o.shipping_service && (
              <>, frete {o.shipping_service === "expresso" ? "expresso" : "econômico"}</>
            )}
            .
          </p>
        </div>

        {s ? (
          <div className="space-y-2">
            <h4 className="font-medium">Entrega</h4>
            <dl className="space-y-1 text-sm">
              <Row label="Nome" value={s.name} />
              <Row label="CPF" value={s.cpf} />
              <Row label="Telefone" value={s.phone} />
              <Row label="CEP" value={s.zip_code} />
              <Row label="Endereço" value={address} />
            </dl>
          </div>
        ) : (
          <p className="text-danger text-sm font-medium">
            Pedido sem endereço: fale com o cliente antes de fazer.
          </p>
        )}
      </div>

      <div className="border-line mt-5 space-y-4 border-t pt-4">
        <PlaceOrderForm orderId={o.order_id} number={number} />
        <div className="flex flex-wrap gap-x-6 gap-y-2">
          <details>
            <summary className="inline-flex min-h-11 cursor-pointer items-center font-semibold pointer-fine:min-h-8">
              O fornecedor não tem o produto<span className="sr-only"> do pedido {number}</span>
            </summary>
            <form action={outOfStock} className="mt-2 max-w-md space-y-2">
              <input type="hidden" name="order_id" value={o.order_id} />
              <p className="text-ink-soft text-sm">
                O pedido é cancelado e o cliente é estornado por inteiro. Tire o produto da próxima planilha
                ou zere o estoque dele.
              </p>
              <SubmitButton variant="danger" size="sm" pendingLabel="Cancelando…">
                Cancelar por falta de estoque
              </SubmitButton>
            </form>
          </details>
          <details>
            <summary className="text-danger inline-flex min-h-11 cursor-pointer items-center font-semibold pointer-fine:min-h-8">
              Cancelar o pedido<span className="sr-only"> {number}</span>
            </summary>
            <form action={cancelManualOrder} className="mt-2 max-w-md space-y-2">
              <input type="hidden" name="order_id" value={o.order_id} />
              <p className="text-ink-soft text-sm">
                O cliente é estornado por inteiro, e o pedido não volta.
              </p>
              <SubmitButton variant="danger" size="sm" pendingLabel="Cancelando…">
                Cancelar e estornar
              </SubmitButton>
            </form>
          </details>
        </div>
      </div>
    </li>
  );
}

function Row({ label, value }: { label: string; value: string }) {
  return (
    <div className="flex items-center gap-1">
      <dt className="text-ink-soft shrink-0">{label}:</dt>
      <dd className="flex min-w-0 flex-1 items-center justify-between gap-2">
        <span className="[overflow-wrap:anywhere]">{value}</span>
        <CopyButton value={value} label={label.toLowerCase()} />
      </dd>
    </div>
  );
}

/** Feito no fornecedor, só a equipe cancela lá: a loja estorna só com a confirmação. */
function CancelPlaced({ order: o }: { order: ManualOrder }) {
  const number = orderNumber(o.order_id);
  return (
    <details>
      <summary className="text-danger inline-flex min-h-11 cursor-pointer items-center font-semibold pointer-fine:min-h-8">
        Cancelar o pedido<span className="sr-only"> {number}</span>
      </summary>
      <form action={cancelManualOrder} className="mt-2 max-w-md space-y-3">
        <input type="hidden" name="order_id" value={o.order_id} />
        <p className="text-ink-soft text-sm">
          O pedido já foi feito no fornecedor ({o.supplier_order_id}). Cancele lá primeiro; depois a loja
          estorna o cliente por inteiro.
        </p>
        <label className="flex min-h-11 items-start gap-2 text-sm">
          <input type="checkbox" name="cancelled_at_supplier" required className="mt-1 size-4" />
          <span>Já cancelei o pedido {o.supplier_order_id} no site do fornecedor.</span>
        </label>
        <SubmitButton variant="danger" size="sm" pendingLabel="Cancelando…">
          Cancelar e estornar
        </SubmitButton>
      </form>
    </details>
  );
}
