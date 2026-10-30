import type { Metadata } from "next";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { supplierReturns, type SupplierReturn } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { orderNumber } from "@/lib/order-status";
import { lateBy } from "@/lib/overdue";

export const metadata: Metadata = { title: "Devoluções" };

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});

// da que pede ação agora à que só espera
const GROUPS = [
  {
    key: "rejected",
    title: "Recusadas pelo fornecedor",
    description: "O cliente já foi estornado, e o custo fica com a loja até você acertar com o fornecedor.",
    has: (r: SupplierReturn) => r.status === "rejected",
  },
  {
    key: "overdue",
    title: "Sem resposta além do prazo",
    description: "O fornecedor não respondeu dentro do prazo. Cobre a resposta pelo código da disputa.",
    has: (r: SupplierReturn) => r.status !== "rejected" && r.overdue,
  },
  {
    key: "waiting",
    title: "Esperando o fornecedor",
    description: "Dentro do prazo. Saem daqui sozinhas quando o fornecedor aprova.",
    has: (r: SupplierReturn) => r.status !== "rejected" && !r.overdue,
  },
] as const;

function situation(r: SupplierReturn): string {
  if (r.status === "rejected") return r.dispute_id ? "Recusada pelo fornecedor" : "Recusada na abertura";
  if (r.status === "open") return "Aberta no fornecedor";
  return r.attempts > 0
    ? `O fornecedor não respondeu (${r.attempts === 1 ? "1 tentativa" : `${r.attempts} tentativas`})`
    : "Na fila para abrir no fornecedor";
}

/**
 * As devoluções ao fornecedor (#223): o cliente desistiu da compra e já foi estornado, e aqui fica a pendência
 * da loja com o fornecedor, com as recusas primeiro. Também mostra a nota de devolução que a SEFAZ rejeitou.
 */
export default async function SupplierReturns() {
  const { token } = await requireAdmin("/admin/devolucoes");
  const { result, readAt: now } = await supplierReturns(token);

  return (
    <div className="max-w-5xl space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Devoluções</h1>
        <p className="text-ink-soft max-w-prose">
          Pedidos que o cliente desistiu de comprar e ainda estão pendentes com o fornecedor. O estorno ao
          cliente já saiu e não depende de nada daqui.
        </p>
      </div>

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="As devoluções não carregaram"
          description={`O serviço do fornecedor não respondeu (${result.error.status}). Tente de novo em instantes.`}
          action={
            <Link href="/admin/devolucoes" className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      ) : result.data.data.length === 0 ? (
        <StateMessage
          title="Nenhuma devolução pendente"
          description="Quando um cliente desistir de uma compra, a devolução ao fornecedor aparece aqui."
        />
      ) : (
        GROUPS.map((g) => {
          const returns = result.data.data.filter(g.has);
          if (returns.length === 0) return null;
          return (
            <section key={g.key} aria-labelledby={`devolucao-${g.key}`} className="space-y-3">
              <div>
                <h2 id={`devolucao-${g.key}`} className="text-xl font-semibold tracking-tight">
                  {g.title}
                  <span className="font-condensed ml-2 tabular-nums">
                    <span className="sr-only">: </span>
                    {returns.length}
                    <span className="sr-only"> {returns.length === 1 ? "devolução" : "devoluções"}</span>
                  </span>
                </h2>
                <p className="text-ink-soft text-sm">{g.description}</p>
              </div>
              <ul className="divide-line border-line divide-y rounded-md border">
                {returns.map((r) => (
                  <ReturnItem key={r.order_id} item={r} now={now} />
                ))}
              </ul>
            </section>
          );
        })
      )}
    </div>
  );
}

function ReturnItem({ item: r, now }: { item: SupplierReturn; now: number }) {
  return (
    <li className="space-y-1 px-4 py-3 text-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4">
        <p className="font-semibold">
          Pedido {orderNumber(r.order_id)}
          <span className="text-ink-soft font-normal">, {situation(r)}</span>
        </p>
        {r.overdue && r.status !== "rejected" && (
          <p className="text-danger font-semibold">
            Prazo venceu <time dateTime={r.due_at}>{lateBy(r.due_at, now)}</time>
          </p>
        )}
      </div>
      <p>{r.items.map((i) => `${i.quantity} × ${i.name}`).join(", ")}</p>
      <p className="text-ink-soft">
        Pedida em <time dateTime={r.requested_at}>{when.format(new Date(r.requested_at))}</time>
        {r.dispute_id && <>; disputa {r.dispute_id}</>}
        {r.return_code && <>; código {r.return_code}</>}
        {r.supplier_order_id && <>; no fornecedor, {r.supplier_order_id}</>}
      </p>
      {r.resolution_reason && <p>Motivo da recusa: {r.resolution_reason}</p>}
      {r.invoice_error && (
        <p className="text-danger font-medium">A SEFAZ rejeitou a nota de devolução: {r.invoice_error}</p>
      )}
      <p>
        <a
          href={`mailto:${r.customer_email}`}
          className="inline-flex min-h-6 items-center font-medium underline underline-offset-4"
        >
          {r.customer_email}
        </a>
      </p>
      {r.last_error && (
        <details>
          <summary className="cursor-pointer font-medium">Último erro</summary>
          <pre className="bg-ink/6 mt-1 overflow-x-auto rounded-sm p-2 text-xs whitespace-pre-wrap">
            {r.last_error}
          </pre>
        </details>
      )}
    </li>
  );
}
