import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { connection } from "next/server";
import { z } from "zod";
import { OrderBadge } from "@/components/account/order-badge";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { requireAccount } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { orderNumber, orderStatus } from "@/lib/order-status";
import { getOrder } from "@/lib/orders-api";

export const metadata: Metadata = { title: "Pedido", robots: { index: false } };

const dateTime = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "long",
  timeStyle: "short",
  timeZone: "America/Sao_Paulo",
});

type Props = { params: Promise<{ id: string }> };

/** Um pedido: a situação (com o motivo, se cancelou), os itens com o preço congelado e o total. */
export default async function OrderPage({ params }: Props) {
  await connection();
  const { id } = await params;
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
  const status = orderStatus(order.status, order.cancellation_reason);

  return (
    <main id="conteudo" className="mx-auto max-w-3xl px-4 pt-6 sm:px-6">
      <Breadcrumbs
        items={[{ href: "/conta", label: "Minha conta" }, { label: `Pedido ${orderNumber(order.id)}` }]}
      />
      <div className="mt-4 space-y-3">
        <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Pedido {orderNumber(order.id)}</h1>
        <p className="text-ink-soft">Feito em {dateTime.format(new Date(order.created_at))}</p>
        <div className="flex flex-wrap items-center gap-3">
          <OrderBadge order={order} />
          <p>{status.detail}</p>
        </div>
      </div>

      <section aria-labelledby="itens" className="mt-8">
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
        <div className="mt-4 flex items-end justify-between">
          <p className="font-semibold">Total</p>
          <p className="font-condensed text-3xl leading-none font-extrabold">{formatMoney(order.total)}</p>
        </div>
      </section>
    </main>
  );
}
