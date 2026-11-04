import { ChevronLeft } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { OrderDetail } from "@/components/admin/orders/order-detail";
import { OrderList } from "@/components/admin/orders/order-list";
import { OrderPages } from "@/components/admin/orders/order-pages";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { getAdminOrder, listAdminOrders } from "@/lib/admin-api";
import { apiFilter, filterSearch, ordersQuery } from "@/lib/admin-orders";
import { requireAdmin } from "@/lib/auth/current";
import { orderNumber } from "@/lib/order-status";

type Props = {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

export async function generateMetadata({ params }: Props): Promise<Metadata> {
  const { id } = await params;
  return { title: `Pedido ${orderNumber(id)}` };
}

const UUID = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

/**
 * Um pedido (#382). Em telas grandes, a lista (com o mesmo filtro) fica à esquerda e o pedido à direita: dá
 * para passar de um pedido a outro sem perder o lugar. Abaixo disso, o pedido ocupa a tela, com a volta à lista.
 */
export default async function OrderPage({ params, searchParams }: Props) {
  const { id } = await params;
  if (!UUID.test(id)) notFound();
  const { token } = await requireAdmin(`/admin/pedidos/${id}`);
  const query = ordersQuery(await searchParams);
  const [order, list] = await Promise.all([
    getAdminOrder(token, id),
    listAdminOrders(token, apiFilter(query)),
  ]);
  if (!order.ok && order.error.status === 404) notFound();
  const back = `/admin/pedidos${filterSearch(query, { depois: query.cursor })}`;

  return (
    <div className="xl:grid xl:grid-cols-[minmax(22rem,28rem)_minmax(0,1fr)] xl:gap-8">
      {/* a lista ao lado só em telas grandes; antes disso, ela é a página anterior */}
      <aside aria-label="Lista de pedidos" className="hidden xl:block">
        <div className="sticky top-8 flex max-h-[calc(100dvh-4rem)] flex-col gap-3">
          <div className="flex items-baseline justify-between gap-3">
            <h2 className="text-lg font-semibold">Pedidos</h2>
            <Link href={back} className="text-sm font-medium underline-offset-4 hover:underline">
              Filtrar a lista
            </Link>
          </div>
          {list.ok && list.data.data.length > 0 ? (
            <div className="-mx-1 overflow-y-auto px-1 pb-1">
              <OrderList
                orders={list.data.data}
                selectedId={id}
                hrefFor={(other) => `/admin/pedidos/${other}${filterSearch(query, { depois: query.cursor })}`}
              />
              <div className="pt-3">
                <OrderPages query={query} next={list.data.page.next_cursor} />
              </div>
            </div>
          ) : (
            <p className="text-ink-soft text-sm">A lista não carregou. O pedido continua ao lado.</p>
          )}
        </div>
      </aside>

      <div className="@container min-w-0 space-y-4">
        <Link href={back} className={`${buttonVariants({ variant: "ghost", size: "sm" })} -ml-3 xl:hidden`}>
          <ChevronLeft aria-hidden="true" />
          Voltar aos pedidos
        </Link>
        {order.ok ? (
          <OrderDetail order={order.data} />
        ) : (
          <StateMessage
            tone="error"
            title="O pedido não carregou"
            description={`O serviço de pedidos não respondeu (${order.error.status}). Tente de novo em instantes.`}
            action={
              <Link
                href={`/admin/pedidos/${id}${filterSearch(query)}`}
                className={buttonVariants({ variant: "secondary" })}
              >
                Tentar de novo
              </Link>
            }
          />
        )}
      </div>
    </div>
  );
}
