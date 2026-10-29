import type { OrdersSchemas } from "@/lib/api/client";

/**
 * A situação do pedido em palavras do cliente, sem os nomes internos da SAGA. O selo sempre leva o
 * texto (nunca só a cor).
 */
type Status = OrdersSchemas["schemas"]["OrderStatus"];
type Reason = OrdersSchemas["schemas"]["CancellationReason"] | null | undefined;

/** O pedido como a SAGA o deixa: situação, motivo e o horário de cada etapa que já aconteceu. */
export type OrderProgress = {
  status: Status;
  cancellation_reason?: Reason;
  stock_reserved_at?: string | null;
  sent_to_supplier_at?: string | null;
  shipped_at?: string | null;
  delivered_at?: string | null;
  refunded_at?: string | null;
  created_at: string;
  updated_at: string;
};

type StatusInput = Pick<
  OrderProgress,
  "status" | "cancellation_reason" | "stock_reserved_at" | "sent_to_supplier_at" | "refunded_at"
>;

export type Tone = "neutral" | "success" | "danger";

function cancelledDetail(reason: Reason, refundedAt?: string | null) {
  switch (reason) {
    case "out_of_stock":
      return "Um dos produtos esgotou antes da reserva. Nada foi cobrado.";
    case "payment_failed":
      return "O pagamento foi recusado. Nada foi cobrado.";
    case "supplier_failed":
      return refundedAt
        ? "O fornecedor não conseguiu atender o pedido. O valor pago foi estornado."
        : "O fornecedor não conseguiu atender o pedido. Estamos estornando o valor pago.";
    default:
      return "O pedido foi cancelado. Nada foi cobrado.";
  }
}

export function orderStatus(order: StatusInput): { label: string; tone: Tone; detail: string } {
  switch (order.status) {
    case "confirmed":
      return {
        label: "Em preparação",
        tone: "neutral",
        detail: order.sent_to_supplier_at
          ? "O fornecedor está separando os produtos."
          : "Enviando o pedido ao fornecedor.",
      };
    case "shipped":
      return { label: "Enviado", tone: "neutral", detail: "O pedido saiu para entrega." };
    case "delivered":
      return { label: "Entregue", tone: "success", detail: "O pedido foi entregue." };
    case "cancelled":
      return {
        label: "Cancelado",
        tone: "danger",
        detail: cancelledDetail(order.cancellation_reason, order.refunded_at),
      };
    default:
      return {
        label: "Processando",
        tone: "neutral",
        detail:
          order.stock_reserved_at === undefined
            ? "Reservando o estoque e processando o pagamento."
            : order.stock_reserved_at
              ? "Estoque reservado. Cobrando o pagamento."
              : "Reservando o estoque.",
      };
  }
}

/** "4F2A9C1B": o começo do id, para o cliente reconhecer o pedido sem ler um UUID inteiro. */
export const orderNumber = (id: string) => id.replace(/-/g, "").slice(0, 8).toUpperCase();

export type StepState = "done" | "current" | "failed" | "skipped" | "upcoming";
export type Step = {
  key: "placed" | "stock" | "payment" | "supplier" | "shipped" | "delivered" | "refund";
  label: string;
  state: StepState;
  at?: string;
};

const paid = (status: Status) => status === "confirmed" || status === "shipped" || status === "delivered";

/**
 * As etapas da compra para o cliente acompanhar, cada uma com o nome do que está acontecendo (ou
 * aconteceu) e o horário real, quando o pedido o tem. Recebido, estoque e pagamento; depois de pago, o
 * fornecedor, o envio e a entrega. Cancelado antes de pagar, a linha para na etapa que falhou; cancelado
 * pelo fornecedor, a entrega dá lugar ao estorno.
 */
export function orderSteps(order: OrderProgress): Step[] {
  const { status, cancellation_reason: reason, stock_reserved_at: reserved } = order;
  const outOfStock = status === "cancelled" && reason === "out_of_stock";
  const paymentFailed = status === "cancelled" && reason === "payment_failed";
  const supplierFailed = status === "cancelled" && reason === "supplier_failed";
  const wasPaid = paid(status) || supplierFailed;
  const end = status === "pending" ? undefined : order.updated_at;

  const placed: Step = { key: "placed", label: "Pedido recebido", state: "done", at: order.created_at };

  const stock: Step = reserved
    ? { key: "stock", label: "Estoque reservado", state: "done", at: reserved }
    : outOfStock
      ? { key: "stock", label: "Sem estoque", state: "failed", at: end }
      : status === "pending"
        ? { key: "stock", label: "Reservando o estoque", state: "current" }
        : // pedido de antes da etapa existir: terminou, então passou pelo estoque (ou foi cancelado sem motivo)
          { key: "stock", label: "Estoque reservado", state: wasPaid ? "done" : "skipped" };

  const payment: Step = wasPaid
    ? { key: "payment", label: "Pagamento aprovado", state: "done" }
    : paymentFailed
      ? { key: "payment", label: "Pagamento recusado", state: "failed", at: end }
      : status === "pending" && reserved
        ? { key: "payment", label: "Cobrando o pagamento", state: "current" }
        : { key: "payment", label: "Pagamento", state: status === "cancelled" ? "skipped" : "upcoming" };

  // antes de pagar, ainda não há fornecedor: a linha mostra só o que vem a seguir de perto
  if (status === "pending" || (status === "cancelled" && !supplierFailed)) {
    return [placed, stock, payment];
  }

  const sent = order.sent_to_supplier_at;
  const supplier: Step = supplierFailed
    ? { key: "supplier", label: "Fornecedor não atendeu", state: "failed", at: end }
    : status === "confirmed"
      ? sent
        ? { key: "supplier", label: "Fornecedor separando", state: "current", at: sent }
        : { key: "supplier", label: "Enviando ao fornecedor", state: "current" }
      : { key: "supplier", label: "Separado pelo fornecedor", state: "done", at: sent ?? undefined };

  if (supplierFailed) {
    const refund: Step = order.refunded_at
      ? { key: "refund", label: "Valor estornado", state: "done", at: order.refunded_at }
      : { key: "refund", label: "Estornando o valor pago", state: "current" };
    return [placed, stock, payment, supplier, refund];
  }

  const shipped: Step =
    status === "shipped"
      ? { key: "shipped", label: "A caminho", state: "current", at: order.shipped_at ?? undefined }
      : status === "delivered"
        ? { key: "shipped", label: "Enviado", state: "done", at: order.shipped_at ?? undefined }
        : { key: "shipped", label: "Envio", state: "upcoming" };

  const delivered: Step =
    status === "delivered"
      ? { key: "delivered", label: "Entregue", state: "done", at: order.delivered_at ?? end }
      : { key: "delivered", label: "Entrega", state: "upcoming" };

  return [placed, stock, payment, supplier, shipped, delivered];
}

/** A manchete da página do pedido: o que está acontecendo agora, numa frase curta. */
export function orderHeadline(order: OrderProgress): string {
  switch (order.status) {
    case "confirmed":
      return "Pagamento aprovado";
    case "shipped":
      return "Pedido a caminho";
    case "delivered":
      return "Pedido entregue";
    case "cancelled":
      return "Pedido cancelado";
    default:
      return order.stock_reserved_at ? "Cobrando o pagamento" : "Reservando o estoque";
  }
}

/** Não muda mais: entregue, ou cancelado sem estorno pendente (o mesmo fim do stream do orders). */
export const isFinal = (order: Pick<OrderProgress, "status" | "cancellation_reason" | "refunded_at">) =>
  order.status === "delivered" ||
  (order.status === "cancelled" && !(order.cancellation_reason === "supplier_failed" && !order.refunded_at));

/** Já saiu da etapa de pagamento (aprovado, ou terminou): a vitrine de etapas da compra acabou. */
export const isSettled = (status: Status) => status !== "pending";
