import type { Metadata } from "next";
import Link from "next/link";
import { OrderFilters } from "@/components/admin/orders/order-filters";
import { OrderList } from "@/components/admin/orders/order-list";
import { OrderPages } from "@/components/admin/orders/order-pages";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { listAdminOrders } from "@/lib/admin-api";
import { apiFilter, filterSearch, ordersQuery } from "@/lib/admin-orders";
import { requireAdmin } from "@/lib/auth/current";

export const metadata: Metadata = { title: "Pedidos" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/**
 * Todos os pedidos (#382), do mais recente para o mais antigo, com o contato de quem comprou. O filtro fica na
 * URL: o detalhe e o "ver os próximos" mantêm a mesma lista.
 */
export default async function Orders({ searchParams }: Props) {
  const { token } = await requireAdmin("/admin/pedidos");
  const query = ordersQuery(await searchParams);
  const result = await listAdminOrders(token, apiFilter(query));
  const filtered = !!(query.q || query.status || query.period.from || query.period.to);

  return (
    <div className="space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Pedidos</h1>
        <p className="text-ink-soft max-w-prose">
          Todos os pedidos da loja, com o contato de quem comprou. Abra um pedido para ver os itens, a entrega
          e o que aconteceu com ele.
        </p>
      </div>

      <OrderFilters query={query} />

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="Os pedidos não carregaram"
          description={`O serviço de pedidos não respondeu (${result.error.status}). Tente de novo em instantes.`}
          action={
            <Link
              href={`/admin/pedidos${filterSearch(query)}`}
              className={buttonVariants({ variant: "secondary" })}
            >
              Tentar de novo
            </Link>
          }
        />
      ) : result.data.data.length === 0 ? (
        <StateMessage
          title={filtered ? "Nenhum pedido com esse filtro" : "Nenhum pedido ainda"}
          description={
            filtered
              ? "Confira o número ou o e-mail, ou mude a situação e o período."
              : "Os pedidos aparecem aqui assim que alguém compra na loja."
          }
          action={
            filtered ? (
              <Link href="/admin/pedidos" className={buttonVariants({ variant: "secondary" })}>
                Ver todos os pedidos
              </Link>
            ) : undefined
          }
        />
      ) : (
        <>
          <OrderList
            orders={result.data.data}
            hrefFor={(id) => `/admin/pedidos/${id}${filterSearch(query, { depois: query.cursor })}`}
          />
          <OrderPages query={query} next={result.data.page.next_cursor} />
        </>
      )}
    </div>
  );
}
