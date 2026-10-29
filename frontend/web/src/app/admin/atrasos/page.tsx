import type { Metadata } from "next";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { overdueOrders, type OverdueOrder } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { orderNumber } from "@/lib/order-status";
import { lateBy } from "@/lib/overdue";

export const metadata: Metadata = { title: "Pedidos atrasados" };

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});
const STAGES = [
  {
    stage: "shipping",
    title: "O fornecedor não despachou",
    description: "Pagos e ainda sem envio depois do prazo de separação. Cobre o fornecedor pelo pedido dele.",
  },
  {
    stage: "delivery",
    title: "O envio não chegou",
    description: "Despachados e ainda sem entrega depois do prazo de transporte. Confira o rastreio.",
  },
] as const;

function situation(o: OverdueOrder): string {
  switch (o.status) {
    case "pending":
      return o.attempts > 0
        ? `O fornecedor não respondeu (${o.attempts === 1 ? "1 tentativa" : `${o.attempts} tentativas`})`
        : "Na fila para o fornecedor";
    case "placing":
      return "Sendo enviado ao fornecedor";
    case "placed":
      return "Aceito pelo fornecedor, sem envio";
    case "shipped":
      return "Em transporte";
  }
}

/**
 * Os pedidos parados além do prazo do fornecedor (#219): os que ele não despachou e os que não chegaram.
 * A loja responde ao cliente pelo prazo que prometeu, então a equipe cobra o fornecedor por aqui.
 */
export default async function OverdueOrders() {
  const { token } = await requireAdmin("/admin/atrasos");
  const { result, readAt: now } = await overdueOrders(token);

  return (
    <div className="max-w-5xl space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Pedidos atrasados</h1>
        <p className="text-ink-soft max-w-prose">
          Pedidos pagos que passaram do prazo do fornecedor, os mais atrasados primeiro. Saem daqui sozinhos
          quando o fornecedor despacha ou entrega.
        </p>
      </div>

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="Os pedidos atrasados não carregaram"
          description={`O serviço do fornecedor não respondeu (${result.error.status}). Tente de novo em instantes.`}
          action={
            <Link href="/admin/atrasos" className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      ) : result.data.data.length === 0 ? (
        <StateMessage
          title="Nenhum pedido atrasado"
          description="Todos os pedidos pagos estão dentro do prazo do fornecedor."
        />
      ) : (
        STAGES.map((s) => {
          const orders = result.data.data.filter((o) => o.stage === s.stage);
          if (orders.length === 0) return null;
          return (
            <section key={s.stage} aria-labelledby={`atraso-${s.stage}`} className="space-y-3">
              <div>
                <h2 id={`atraso-${s.stage}`} className="text-xl font-semibold tracking-tight">
                  {s.title}
                  <span className="font-condensed text-danger ml-2 tabular-nums">
                    <span className="sr-only">: </span>
                    {orders.length}
                    <span className="sr-only"> {orders.length === 1 ? "pedido" : "pedidos"}</span>
                  </span>
                </h2>
                <p className="text-ink-soft text-sm">{s.description}</p>
              </div>
              <ul className="divide-line border-line divide-y rounded-md border">
                {orders.map((o) => (
                  <OverdueItem key={o.order_id} order={o} now={now} />
                ))}
              </ul>
            </section>
          );
        })
      )}
    </div>
  );
}

function OverdueItem({ order: o, now }: { order: OverdueOrder; now: number }) {
  return (
    <li className="space-y-1 px-4 py-3 text-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4">
        <p className="font-semibold">
          Pedido {orderNumber(o.order_id)}
          <span className="text-ink-soft font-normal">, {situation(o)}</span>
        </p>
        <p className="text-danger font-semibold">
          Prazo venceu <time dateTime={o.due_at}>{lateBy(o.due_at, now)}</time>
        </p>
      </div>
      <p className="text-ink-soft">
        Pago em <time dateTime={o.created_at}>{when.format(new Date(o.created_at))}</time>
        {o.shipped_at && (
          <>
            , enviado em <time dateTime={o.shipped_at}>{when.format(new Date(o.shipped_at))}</time>
          </>
        )}
        {o.supplier_order_id && <>; no fornecedor, {o.supplier_order_id}</>}
      </p>
      <p className="flex flex-wrap gap-x-4 gap-y-2">
        <a
          href={`mailto:${o.customer_email}`}
          className="inline-flex min-h-6 items-center font-medium underline underline-offset-4"
        >
          {o.customer_email}
        </a>
        {o.tracking &&
          (o.tracking.url ? (
            <a
              href={o.tracking.url}
              target="_blank"
              rel="noreferrer"
              className="inline-flex min-h-6 items-center font-medium underline underline-offset-4"
            >
              Rastreio {o.tracking.number}
              <span className="sr-only"> (abre em outra aba)</span>
            </a>
          ) : (
            <span className="inline-flex min-h-6 items-center">Rastreio {o.tracking.number}</span>
          ))}
      </p>
      {o.last_error && (
        <details>
          <summary className="cursor-pointer font-medium">Último erro</summary>
          <pre className="bg-ink/6 mt-1 overflow-x-auto rounded-sm p-2 text-xs whitespace-pre-wrap">
            {o.last_error}
          </pre>
        </details>
      )}
    </li>
  );
}
