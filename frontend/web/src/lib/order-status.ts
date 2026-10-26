/**
 * A situação do pedido em palavras do cliente, sem os nomes internos da SAGA. O selo sempre leva o
 * texto (nunca só a cor).
 */
type Status = "pending" | "confirmed" | "cancelled";
type Reason = "out_of_stock" | "payment_failed" | null | undefined;

export function orderStatus(status: Status, reason?: Reason) {
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
        detail: "Reservando o estoque e processando o pagamento.",
      };
  }
}

/** "4F2A9C1B": o começo do id, para o cliente reconhecer o pedido sem ler um UUID inteiro. */
export const orderNumber = (id: string) => id.replace(/-/g, "").slice(0, 8).toUpperCase();
