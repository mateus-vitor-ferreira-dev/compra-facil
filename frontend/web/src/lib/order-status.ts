import type { OrdersSchemas } from "@/lib/api/client";

/**
 * A situação do pedido em palavras do cliente, sem os nomes internos da SAGA. O selo sempre leva o
 * texto (nunca só a cor).
 */
type Status = OrdersSchemas["schemas"]["OrderStatus"];
type Reason = OrdersSchemas["schemas"]["CancellationReason"] | null | undefined;
/** Como o cliente paga (#220): o PIX, depois de gerado, traz o código e a validade. */
type Payment = Pick<OrdersSchemas["schemas"]["OrderPayment"], "method"> & {
  pix?: OrdersSchemas["schemas"]["OrderPayment"]["pix"];
};

/** O pedido como a SAGA o deixa: situação, motivo e o horário de cada etapa que já aconteceu. */
export type OrderProgress = {
  status: Status;
  cancellation_reason?: Reason;
  stock_reserved_at?: string | null;
  sent_to_supplier_at?: string | null;
  shipped_at?: string | null;
  delivered_at?: string | null;
  refunded_at?: string | null;
  /** O cliente se arrependeu (#223): pediu a devolução dentro dos 7 dias da entrega. */
  return_requested_at?: string | null;
  payment?: Payment;
  created_at: string;
  updated_at: string;
};

type StatusInput = Pick<
  OrderProgress,
  "status" | "cancellation_reason" | "stock_reserved_at" | "sent_to_supplier_at" | "refunded_at" | "payment"
>;

/** O pedido espera o cliente pagar o PIX: o código já foi gerado. */
export const awaitingPix = (order: Pick<OrderProgress, "status" | "payment">) =>
  order.status === "pending" && !!order.payment?.pix;

export type Tone = "neutral" | "success" | "danger";

function cancelledDetail(reason: Reason, refundedAt?: string | null, method?: Payment["method"]) {
  switch (reason) {
    case "out_of_stock":
      return "Um dos produtos esgotou antes da reserva. Nada foi cobrado.";
    case "payment_failed":
      // no PIX, a única falha é o prazo: o cliente não pagou a tempo
      return method === "pix"
        ? "O PIX venceu sem pagamento. Nada foi cobrado."
        : method === "card"
          ? "O cartão foi recusado. Nada foi cobrado."
          : "O pagamento foi recusado. Nada foi cobrado.";
    case "supplier_failed":
      return refundedAt
        ? "O fornecedor não conseguiu atender o pedido. O valor pago foi estornado."
        : "O fornecedor não conseguiu atender o pedido. Estamos estornando o valor pago.";
    case "invoice_failed":
      return refundedAt
        ? "Não conseguimos emitir a nota fiscal do pedido. O valor pago foi estornado."
        : "Não conseguimos emitir a nota fiscal do pedido. Estamos estornando o valor pago.";
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
          : order.payment?.method === "card"
            ? "Enviando o pedido ao fornecedor. O cartão só é cobrado quando ele aceitar."
            : "Enviando o pedido ao fornecedor.",
      };
    case "shipped":
      return { label: "Enviado", tone: "neutral", detail: "O pedido saiu para entrega." };
    case "delivered":
      return { label: "Entregue", tone: "success", detail: "O pedido foi entregue." };
    case "returned":
      return {
        label: "Devolvido",
        tone: "neutral",
        detail: order.refunded_at
          ? "Você desistiu da compra. O valor pago, com o frete, foi estornado."
          : "Você desistiu da compra. Estamos estornando o valor pago, com o frete.",
      };
    case "cancelled":
      return {
        label: "Cancelado",
        tone: "danger",
        detail: cancelledDetail(order.cancellation_reason, order.refunded_at, order.payment?.method),
      };
    default:
      if (awaitingPix(order)) {
        return {
          label: "Aguardando o PIX",
          tone: "neutral",
          detail: "O pedido é confirmado assim que o pagamento cair.",
        };
      }
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
  key: "placed" | "stock" | "payment" | "supplier" | "shipped" | "delivered" | "return" | "refund";
  label: string;
  state: StepState;
  at?: string;
};

const paid = (status: Status) =>
  status === "confirmed" || status === "shipped" || status === "delivered" || status === "returned";

/**
 * As etapas da compra para o cliente acompanhar, cada uma com o nome do que está acontecendo (ou
 * aconteceu) e o horário real, quando o pedido o tem. Recebido, estoque e pagamento; depois de pago, o
 * fornecedor, o envio e a entrega. Cancelado antes de pagar, a linha para na etapa que falhou; cancelado
 * pelo fornecedor, a entrega dá lugar ao estorno. Devolvido (#223), a entrega ganha a devolução e o estorno.
 */
export function orderSteps(order: OrderProgress): Step[] {
  const { status, cancellation_reason: reason, stock_reserved_at: reserved } = order;
  const outOfStock = status === "cancelled" && reason === "out_of_stock";
  const paymentFailed = status === "cancelled" && reason === "payment_failed";
  // cancelado depois de pago: pelo fornecedor, ou porque a nota fiscal foi rejeitada (#221)
  const invoiceFailed = status === "cancelled" && reason === "invoice_failed";
  const supplierFailed = status === "cancelled" && (reason === "supplier_failed" || invoiceFailed);
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
      ? {
          key: "payment",
          label: order.payment?.method === "pix" ? "PIX vencido" : "Pagamento recusado",
          state: "failed",
          at: end,
        }
      : awaitingPix(order)
        ? { key: "payment", label: "Esperando o PIX", state: "current" }
        : status === "pending" && reserved
          ? { key: "payment", label: "Cobrando o pagamento", state: "current" }
          : { key: "payment", label: "Pagamento", state: status === "cancelled" ? "skipped" : "upcoming" };

  // antes de pagar, ainda não há fornecedor: a linha mostra só o que vem a seguir de perto
  if (status === "pending" || (status === "cancelled" && !supplierFailed)) {
    return [placed, stock, payment];
  }

  const sent = order.sent_to_supplier_at;
  const supplier: Step = supplierFailed
    ? {
        key: "supplier",
        label: invoiceFailed ? "Nota fiscal recusada" : "Fornecedor não atendeu",
        state: "failed",
        at: end,
      }
    : status === "confirmed"
      ? sent
        ? { key: "supplier", label: "Fornecedor separando", state: "current", at: sent }
        : { key: "supplier", label: "Enviando ao fornecedor", state: "current" }
      : { key: "supplier", label: "Separado pelo fornecedor", state: "done", at: sent ?? undefined };

  const refund: Step = order.refunded_at
    ? { key: "refund", label: "Valor estornado", state: "done", at: order.refunded_at }
    : { key: "refund", label: "Estornando o valor pago", state: "current" };

  if (supplierFailed) {
    return [placed, stock, payment, supplier, refund];
  }

  const arrived = status === "delivered" || status === "returned";
  const shipped: Step =
    status === "shipped"
      ? { key: "shipped", label: "A caminho", state: "current", at: order.shipped_at ?? undefined }
      : arrived
        ? { key: "shipped", label: "Enviado", state: "done", at: order.shipped_at ?? undefined }
        : { key: "shipped", label: "Envio", state: "upcoming" };

  const delivered: Step =
    status === "delivered"
      ? { key: "delivered", label: "Entregue", state: "done", at: order.delivered_at ?? end }
      : status === "returned"
        ? { key: "delivered", label: "Entregue", state: "done", at: order.delivered_at ?? undefined }
        : { key: "delivered", label: "Entrega", state: "upcoming" };

  if (status === "returned") {
    const asked: Step = {
      key: "return",
      label: "Devolução pedida",
      state: "done",
      at: order.return_requested_at ?? undefined,
    };
    return [placed, stock, payment, supplier, shipped, delivered, asked, refund];
  }

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
    case "returned":
      return order.refunded_at ? "Valor estornado" : "Devolução pedida";
    case "cancelled":
      return "Pedido cancelado";
    default:
      if (awaitingPix(order)) return "Pague o PIX";
      return order.stock_reserved_at ? "Cobrando o pagamento" : "Reservando o estoque";
  }
}

/**
 * Não muda mais sozinho: entregue (só o cliente devolve), devolvido e estornado, ou cancelado sem estorno
 * pendente (o mesmo fim do stream do orders).
 */
export const isFinal = (order: Pick<OrderProgress, "status" | "cancellation_reason" | "refunded_at">) =>
  order.status === "delivered" ||
  (order.status === "returned" && !!order.refunded_at) ||
  (order.status === "cancelled" && !(paidThenCancelled(order.cancellation_reason) && !order.refunded_at));

/** Cancelado depois de pago (o fornecedor ou a nota): o estorno ainda pode estar a caminho. */
const paidThenCancelled = (reason: Reason) => reason === "supplier_failed" || reason === "invoice_failed";

/** Já saiu da etapa de pagamento (aprovado, ou terminou): a vitrine de etapas da compra acabou. */
export const isSettled = (status: Status) => status !== "pending";

/**
 * O link da transportadora (#224), só se for http(s): a URL vem do fornecedor e não pode virar um
 * `javascript:` na página do cliente.
 */
export function trackingHref(url: string | null | undefined): string | null {
  if (!url) return null;
  try {
    const parsed = new URL(url);
    return parsed.protocol === "https:" || parsed.protocol === "http:" ? parsed.href : null;
  } catch {
    return null;
  }
}

/** Com rastreio para mostrar: a caminho ou entregue (devolvido, o rastreio já não ajuda). */
export const showsTracking = (status: Status) => status === "shipped" || status === "delivered";
