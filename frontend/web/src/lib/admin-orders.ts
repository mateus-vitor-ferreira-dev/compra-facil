import type { components } from "@/lib/api/schema/orders";
import { marginPeriod, type MarginPeriod } from "@/lib/margin-period";

type Schemas = components["schemas"];
type Status = Schemas["OrderStatus"];
type Reason = Schemas["CancellationReason"];
export type Tone = "neutral" | "success" | "danger";

/** As situações do filtro, na ordem da vida do pedido. */
export const STATUS_FILTERS: { value: Status; label: string }[] = [
  { value: "pending", label: "Aguardando pagamento" },
  { value: "confirmed", label: "Em preparação" },
  { value: "shipped", label: "Enviados" },
  { value: "delivered", label: "Entregues" },
  { value: "returned", label: "Devolvidos" },
  { value: "cancelled", label: "Cancelados" },
];

const CANCELLED: Record<Reason, string> = {
  out_of_stock: "Cancelado: sem estoque",
  payment_failed: "Cancelado: não pago",
  supplier_failed: "Cancelado pelo fornecedor",
  invoice_failed: "Cancelado: nota recusada",
  store_cancelled: "Cancelado pela loja",
};

/** A situação para a equipe: curta, e no cancelado, o porquê (o selo leva o texto, não só a cor). */
export function adminOrderStatus(o: { status: Status; cancellation_reason?: Reason | null }): {
  label: string;
  tone: Tone;
} {
  switch (o.status) {
    case "pending":
      return { label: "Aguardando pagamento", tone: "neutral" };
    case "confirmed":
      return { label: "Em preparação", tone: "neutral" };
    case "shipped":
      return { label: "Enviado", tone: "neutral" };
    case "delivered":
      return { label: "Entregue", tone: "success" };
    case "returned":
      return { label: "Devolvido", tone: "neutral" };
    case "cancelled":
      return {
        label: o.cancellation_reason ? CANCELLED[o.cancellation_reason] : "Cancelado",
        tone: "danger",
      };
  }
}

export type OrdersQuery = {
  q: string;
  status?: Status;
  period: MarginPeriod;
  cursor?: string;
};

const one = (v: string | string[] | undefined) => (typeof v === "string" ? v : undefined);
const isStatus = (v: string | undefined): v is Status => STATUS_FILTERS.some((s) => s.value === v);

/** O filtro da lista, da URL (?q=&situacao=&de=&ate=&depois=): o que não vale é ignorado. */
export function ordersQuery(params: Record<string, string | string[] | undefined>): OrdersQuery {
  const status = one(params.situacao);
  return {
    q: (one(params.q) ?? "").trim().slice(0, 100),
    status: isStatus(status) ? status : undefined,
    period: marginPeriod({ de: one(params.de), ate: one(params.ate) }),
    cursor: one(params.depois),
  };
}

/** O filtro como a API recebe; com o período inválido, a lista mostra todos (e o formulário avisa). */
export function apiFilter(q: OrdersQuery) {
  return {
    q: q.q || undefined,
    status: q.status,
    from: q.period.error ? undefined : q.period.from,
    to: q.period.error ? undefined : q.period.to,
    cursor: q.cursor,
  };
}

/** A query string do filtro, para os links da lista e do detalhe manterem a mesma lista. */
export function filterSearch(q: OrdersQuery, extra: Record<string, string | undefined> = {}): string {
  const s = new URLSearchParams();
  if (q.q) s.set("q", q.q);
  if (q.status) s.set("situacao", q.status);
  if (q.period.de) s.set("de", q.period.de);
  if (q.period.ate) s.set("ate", q.period.ate);
  for (const [k, v] of Object.entries(extra)) if (v) s.set(k, v);
  const text = s.toString();
  return text ? `?${text}` : "";
}

/** O WhatsApp do cliente: o número brasileiro com o 55 na frente. */
export function whatsappHref(phone: string | null | undefined): string | null {
  const digits = (phone ?? "").replace(/\D/g, "");
  return digits.length === 10 || digits.length === 11 ? `https://wa.me/55${digits}` : null;
}
