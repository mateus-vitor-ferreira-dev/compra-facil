import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { z } from "zod";
import { OrderLive } from "@/components/account/order-live";
import { ReturnOrder } from "@/components/account/return-order";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { requireAccount } from "@/lib/auth/current";
import { addressLines } from "@/lib/delivery";
import { promised } from "@/lib/shipping";
import { formatMoney } from "@/lib/format";
import { orderNumber } from "@/lib/order-status";
import { getOrder } from "@/lib/orders-api";

export const metadata: Metadata = { title: "Pedido", robots: { index: false } };

const dateTime = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "long",
  timeStyle: "short",
  timeZone: "America/Sao_Paulo",
});

type Props = {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/**
 * Um pedido: a compra andando ao vivo (ou como terminou, com o motivo se cancelou), os itens com o
 * preço congelado e o total. `?novo=1`: o cliente acabou de fazer o pedido no checkout; `?desistencia=1`,
 * acabou de desistir dele (#223).
 */
export default async function OrderPage({ params, searchParams }: Props) {
  await connection();
  const { id } = await params;
  const query = await searchParams;
  const justPlaced = query.novo === "1";
  const justReturned = query.desistencia === "1";
  const { token } = await requireAccount(`/conta/pedidos/${id}`);
  if (!z.uuid().safeParse(id).success) notFound();
  const result = await getOrder(token, id);
  if (!result.ok && result.error.status === 404) notFound();

  if (!result.ok) {
    return (
      <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-8 sm:px-6">
        <StateMessage
          tone="error"
          title="O pedido não carregou"
          description="O serviço de pedidos não respondeu agora. Tente de novo em instantes."
          action={
            <Link href={`/conta/pedidos/${id}`} className={buttonVariants({ variant: "secondary" })}>
              Tentar de novo
            </Link>
          }
        />
      </main>
    );
  }

  const order = result.data;

  return (
    <main id="conteudo" className="mx-auto max-w-3xl px-4 pt-6 sm:px-6">
      <Breadcrumbs
        items={[{ href: "/conta", label: "Minha conta" }, { label: `Pedido ${orderNumber(order.id)}` }]}
      />
      <div className="mt-4 space-y-1">
        {justPlaced && <p className="font-semibold">Recebemos o seu pedido.</p>}
        {justReturned && order.status === "returned" && (
          <p role="status" className="font-semibold">
            Desistência registrada. O estorno já foi pedido.
          </p>
        )}
        <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Pedido {orderNumber(order.id)}</h1>
        <p className="text-ink-soft">Feito em {dateTime.format(new Date(order.created_at))}</p>
      </div>

      <div className="mt-8">
        {/* a desistência muda o pedido parado na entrega: remontado, o stream reabre e mostra o estorno */}
        <OrderLive key={order.status} initial={order} />
      </div>

      <section aria-labelledby="itens" className="mt-10">
        <h2 id="itens" className="text-xl font-semibold tracking-tight">
          Produtos
        </h2>
        <ul className="divide-line border-line mt-3 divide-y border-y">
          {order.items.map((item) => (
            <li key={item.product_id} className="flex items-start justify-between gap-4 py-4">
              <div>
                <Link href={`/produto/${item.product_id}`} className="font-medium hover:underline">
                  {item.name}
                </Link>
                <p className="text-ink-soft text-sm">
                  {item.quantity} × {formatMoney(item.unit_price)}
                </p>
              </div>
              <p className="font-condensed text-lg font-extrabold">{formatMoney(item.line_total)}</p>
            </li>
          ))}
        </ul>
        <dl className="mt-4 space-y-2">
          {order.shipping && (
            <>
              <div className="flex justify-between gap-4">
                <dt className="text-ink-soft">Produtos</dt>
                <dd className="tabular-nums">{formatMoney(order.items_total)}</dd>
              </div>
              <div className="flex justify-between gap-4">
                <dt className="text-ink-soft">Frete {order.shipping.name.toLowerCase()}</dt>
                <dd className="tabular-nums">
                  {order.shipping.price.amount_cents === 0 ? "Grátis" : formatMoney(order.shipping.price)}
                </dd>
              </div>
            </>
          )}
          <div className="flex items-end justify-between gap-4">
            <dt className="font-semibold">Total</dt>
            <dd className="font-condensed text-3xl leading-none font-extrabold">
              {formatMoney(order.total)}
            </dd>
          </div>
        </dl>
      </section>

      {order.recipient && order.shipping_address && (
        <section aria-labelledby="entrega" className="mt-10 space-y-1">
          <h2 id="entrega" className="text-xl font-semibold tracking-tight">
            Entrega
          </h2>
          {order.shipping &&
            order.status !== "cancelled" &&
            order.status !== "delivered" &&
            order.status !== "returned" && (
              // a data que a loja prometeu: é por ela que o cliente cobra, e o painel mede o atraso
              <p className="font-condensed pb-1 text-2xl font-extrabold tracking-tight">
                Chega até {promised(order.shipping.promised_by)}
              </p>
            )}
          <p className="font-medium">{order.recipient.name}</p>
          <address className="text-ink-soft not-italic">
            {addressLines(order.shipping_address).map((line) => (
              <span key={line} className="block">
                {line}
              </span>
            ))}
          </address>
        </section>
      )}

      {order.status === "delivered" && <ReturnOrder order={order} />}

      {order.invoice && (
        <section aria-labelledby="nota" className="mt-10 space-y-1">
          <h2 id="nota" className="text-xl font-semibold tracking-tight">
            Nota fiscal
          </h2>
          <p>
            NF-e {order.invoice.number}, série {order.invoice.series}, emitida em{" "}
            <time dateTime={order.invoice.issued_at}>
              {dateTime.format(new Date(order.invoice.issued_at))}
            </time>
            {order.invoice.cancelled_at && <span className="text-ink-soft"> (cancelada no estorno)</span>}
          </p>
          <p className="pt-1">
            <a
              href={`/api/pedidos/${order.id}/nota`}
              download
              className="inline-flex min-h-11 items-center font-semibold underline underline-offset-4 sm:min-h-8"
            >
              Baixar a nota (PDF)
            </a>
          </p>
          <p className="text-ink-soft text-sm">
            Chave de acesso{" "}
            <span className="text-ink tabular-nums select-all">
              {order.invoice.access_key.replace(/(\d{4})(?=\d)/g, "$1 ")}
            </span>
          </p>
        </section>
      )}

      {order.return_invoice && (
        <section aria-labelledby="nota-devolucao" className="mt-10 space-y-1">
          <h2 id="nota-devolucao" className="text-xl font-semibold tracking-tight">
            Nota de devolução
          </h2>
          <p>
            NF-e {order.return_invoice.number}, série {order.return_invoice.series}, emitida em{" "}
            <time dateTime={order.return_invoice.issued_at}>
              {dateTime.format(new Date(order.return_invoice.issued_at))}
            </time>
          </p>
          <p className="pt-1">
            <a
              href={`/api/pedidos/${order.id}/nota?tipo=devolucao`}
              download
              className="inline-flex min-h-11 items-center font-semibold underline underline-offset-4 sm:min-h-8"
            >
              Baixar a nota de devolução (PDF)
            </a>
          </p>
          <p className="text-ink-soft text-sm">
            Chave de acesso{" "}
            <span className="text-ink tabular-nums select-all">
              {order.return_invoice.access_key.replace(/(\d{4})(?=\d)/g, "$1 ")}
            </span>
          </p>
        </section>
      )}

      <div className="mt-8 flex flex-wrap gap-3">
        <Link href="/" className={buttonVariants({ variant: "secondary" })}>
          Continuar comprando
        </Link>
        <Link href="/conta" className={buttonVariants({ variant: "ghost" })}>
          Ver meus pedidos
        </Link>
      </div>
    </main>
  );
}
