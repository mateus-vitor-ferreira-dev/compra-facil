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

/**
 * Desiste da compra (#223): o pedido entregue há até 7 dias passa a devolvido e o pagamento é estornado.
 * Pedir de novo devolve o mesmo pedido; fora do prazo (ou ainda não entregue) dá 409.
 */
export function returnOrder(token: string, id: string): Promise<Result<Order>> {
  return call(async () => {
    const r = await api.orders({ token }).POST("/v1/orders/{order_id}/return", {
      params: { path: { order_id: id } },
    });
    return { data: r.data, response: r.response };
  });
}

export type PlaceOrderItem = { product_id: string; quantity: number; expected_unit_price_cents: number };
/** PIX, ou o cartão já tokenizado no provedor (#220): o número dele nunca chega aqui. */
export type PlaceOrderPayment = OrdersSchemas["schemas"]["PlaceOrderPayment"];
/** Quem recebe, onde (#221) e o frete escolhido (#222): o CPF, o telefone e o CEP só com os dígitos. */
export type PlaceOrderDelivery = Pick<
  OrdersSchemas["schemas"]["PlaceOrderRequest"],
  "recipient" | "shipping_address" | "shipping"
>;

/** Por que o pedido não foi criado, com o que a tela precisa para explicar e o suporte, para achar. */
export type PlaceOrderFailure = {
  status: number;
  /** 503: segundos até valer a pena tentar de novo */
  retryAfter?: number;
  /** o trace da requisição no Grafana (Tempo), para o cliente informar ao suporte */
  traceId?: string;
};

const TRACE_ID = /^[0-9a-f]{32}$/;

/**
 * Cria o pedido (202, já `pending`) e inicia a SAGA. A mesma `Idempotency-Key` devolve o mesmo pedido:
 * o clique duplo ou o reenvio do formulário não criam dois.
 */
export async function placeOrder(
  token: string,
  items: PlaceOrderItem[],
  payment: PlaceOrderPayment,
  delivery: PlaceOrderDelivery,
  idempotencyKey: string,
): Promise<{ ok: true; order: Order } | { ok: false; error: PlaceOrderFailure }> {
  try {
    const r = await api.orders({ token }).POST("/v1/orders", {
      params: { header: { "Idempotency-Key": idempotencyKey } },
      body: { items, payment, ...delivery },
      cache: "no-store",
    });
    if (r.data) return { ok: true, order: r.data };
    const retry = Number(r.response.headers.get("retry-after"));
    const trace =
      (r.error as { trace_id?: string } | undefined)?.trace_id ?? r.response.headers.get("x-trace-id");
    return {
      ok: false,
      error: {
        status: r.response.status,
        retryAfter: Number.isFinite(retry) && retry > 0 ? retry : undefined,
        traceId: trace && TRACE_ID.test(trace) ? trace : undefined,
      },
    };
  } catch {
    return { ok: false, error: { status: 503 } }; // gateway fora do ar
  }
}
