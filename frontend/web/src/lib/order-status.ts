/**
 * A situação do pedido em palavras do cliente, sem os nomes internos da SAGA. O selo sempre leva o
 * texto (nunca só a cor).
 */
type Status = "pending" | "confirmed" | "cancelled";
type Reason = "out_of_stock" | "payment_failed" | null | undefined;

export function orderStatus(status: Status, reason?: Reason, stockReservedAt?: string | null) {
  switch (status) {
    case "confirmed":
      return {
        label: "Confirmado",
        tone: "success" as const,
        detail: "Pagamento aprovado e estoque reservado.",
      };
    case "cancelled":
      return {
        label: "Cancelado",
        tone: "danger" as const,
        detail:
          reason === "out_of_stock"
            ? "Um dos produtos esgotou antes da reserva. Nada foi cobrado."
            : reason === "payment_failed"
              ? "O pagamento foi recusado. Nada foi cobrado."
              : "O pedido foi cancelado. Nada foi cobrado.",
      };
    default:
      return {
        label: "Processando",
        tone: "neutral" as const,
        detail:
          stockReservedAt === undefined
            ? "Reservando o estoque e processando o pagamento."
            : stockReservedAt
              ? "Estoque reservado. Cobrando o pagamento."
              : "Reservando o estoque.",
      };
  }
}

/** "4F2A9C1B": o começo do id, para o cliente reconhecer o pedido sem ler um UUID inteiro. */
export const orderNumber = (id: string) => id.replace(/-/g, "").slice(0, 8).toUpperCase();

/** O pedido como a SAGA o deixa: situação, motivo e a etapa (`stock_reserved_at`) enquanto pendente. */
export type OrderProgress = {
  status: Status;
  cancellation_reason?: Reason;
  stock_reserved_at?: string | null;
  created_at: string;
  updated_at: string;
};

export type StepState = "done" | "current" | "failed" | "skipped" | "upcoming";
export type Step = {
  key: "placed" | "stock" | "payment" | "confirmed";
  label: string;
  state: StepState;
  at?: string;
};

/**
 * As etapas da compra para o cliente acompanhar: recebido, estoque, pagamento, confirmado. Cada uma com
 * o nome do que está acontecendo (ou aconteceu) e o horário real, quando o pedido o tem.
 */
export function orderSteps(order: OrderProgress): Step[] {
  const { status, cancellation_reason: reason, stock_reserved_at: reserved } = order;
  const outOfStock = status === "cancelled" && reason === "out_of_stock";
  const paymentFailed = status === "cancelled" && reason === "payment_failed";
  const end = status === "pending" ? undefined : order.updated_at;

  const stock: Step = reserved
    ? { key: "stock", label: "Estoque reservado", state: "done", at: reserved }
    : outOfStock
      ? { key: "stock", label: "Sem estoque", state: "failed", at: end }
      : status === "pending"
        ? { key: "stock", label: "Reservando o estoque", state: "current" }
        : // pedido de antes da etapa existir: terminou, então passou pelo estoque (ou foi cancelado sem motivo)
          { key: "stock", label: "Estoque reservado", state: status === "confirmed" ? "done" : "skipped" };

  const payment: Step =
    status === "confirmed"
      ? { key: "payment", label: "Pagamento aprovado", state: "done", at: end }
      : paymentFailed
        ? { key: "payment", label: "Pagamento recusado", state: "failed", at: end }
        : status === "pending" && reserved
          ? { key: "payment", label: "Cobrando o pagamento", state: "current" }
          : { key: "payment", label: "Pagamento", state: status === "cancelled" ? "skipped" : "upcoming" };

  const confirmed: Step =
    status === "confirmed"
      ? { key: "confirmed", label: "Pedido confirmado", state: "done", at: end }
      : { key: "confirmed", label: "Confirmação", state: status === "cancelled" ? "skipped" : "upcoming" };

  return [
    { key: "placed", label: "Pedido recebido", state: "done", at: order.created_at },
    stock,
    payment,
    confirmed,
  ];
}

/** A manchete da página do pedido: o que está acontecendo agora, numa frase curta. */
export function orderHeadline(order: OrderProgress): string {
  if (order.status === "confirmed") return "Pedido confirmado";
  if (order.status === "cancelled") return "Pedido cancelado";
  return order.stock_reserved_at ? "Cobrando o pagamento" : "Reservando o estoque";
}

export const isFinal = (status: Status) => status !== "pending";
