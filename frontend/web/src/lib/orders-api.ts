import "server-only";
import { api, type OrdersSchemas } from "@/lib/api/client";
import { call, type Result } from "@/lib/catalog-api";

export type Order = OrdersSchemas["schemas"]["Order"];
export type OrderPage = { data: Order[]; page: { next_cursor?: string | null; has_more: boolean } };

/** "Meus pedidos": os do cliente do token, do mais recente para o mais antigo. Sem cache: são dele. */
export function listMyOrders(token: string, cursor?: string): Promise<Result<OrderPage>> {
  return call(async () => {
    const r = await api.orders({ token }).GET("/v1/me/orders", {
      params: { query: { cursor, limit: 20 } },
      cache: "no-store",
    });
    return { data: r.data, response: r.response };
  });
}

/** Um pedido do cliente; o de outro cliente dá 404, como se não existisse. */
export function getOrder(token: string, id: string): Promise<Result<Order>> {
  return call(async () => {
    const r = await api.orders({ token }).GET("/v1/orders/{order_id}", {
      params: { path: { order_id: id } },
      cache: "no-store",
    });
    return { data: r.data, response: r.response };
  });
}
