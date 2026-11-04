import type { components as Fulfillment } from "@/lib/api/schema/fulfillment";
import type { components as Orders } from "@/lib/api/schema/orders";
import { dayOf, marginPeriod, presets } from "@/lib/margin-period";

type Summary = Orders["schemas"]["SalesSummary"];
type MarginTotals = Fulfillment["schemas"]["OrderMarginTotals"];

const DAY = 24 * 60 * 60 * 1000;
export const MAX_DAYS = 366;

export type OverviewPeriod = { de: string; ate: string; from: string; to: string; error?: string };

/**
 * O período da visão geral: o da URL (?de=&ate=, os dois dias incluídos), ou os últimos 30 dias. Diferente das
 * margens, aqui não há "todos os pedidos": o resumo vai até um ano. Período inválido mostra os 30 dias e avisa.
 */
export function overviewPeriod(query: { de?: string; ate?: string }, now = Date.now()): OverviewPeriod {
  const preset = presets(now)[1]!; // últimos 30 dias
  const base = marginPeriod({ de: preset.de, ate: preset.ate });
  const fallback = { de: base.de, ate: base.ate, from: base.from!, to: base.to! };
  if (!query.de && !query.ate) return fallback;
  const asked = marginPeriod(query);
  const error =
    asked.error ??
    (!asked.from || !asked.to
      ? "Escolha o início e o fim do período."
      : Date.parse(asked.to) - Date.parse(asked.from) > MAX_DAYS * DAY
        ? `O período vai até ${MAX_DAYS} dias.`
        : undefined);
  if (error) return { ...fallback, de: query.de ?? "", ate: query.ate ?? "", error };
  return { de: asked.de, ate: asked.ate, from: asked.from!, to: asked.to! };
}

export type ReceiptLine = { label: string; cents: number; detail?: string };

/**
 * O extrato do período, em ordem: do que os clientes pagaram até o que sobrou. O custo, o frete e a taxa vêm
 * das margens do fulfillment, que só somam os pedidos com a conta fechada; o que ainda não tem a conta vira uma
 * linha própria, para a soma bater sem inventar margem para pedido sem custo.
 */
export function receipt(
  summary: Summary,
  margins: MarginTotals | null,
): { lines: ReceiptLine[]; left: number } {
  const gross = summary.gross_sales.amount_cents;
  const refunds = summary.refunds.amount_cents;
  const lines: ReceiptLine[] = [
    { label: "Vendas brutas", cents: gross, detail: `${summary.orders.paid} pagos, com o frete cobrado` },
    {
      label: "Estornos",
      cents: -refunds,
      detail: `${summary.orders.refunded} devolvidos ou cancelados depois de pagos`,
    },
  ];
  if (!margins) return { lines, left: gross - refunds };
  const open = gross - refunds - margins.revenue_cents;
  if (open !== 0) {
    lines.push({
      label: "Ainda sem a conta fechada",
      cents: -open,
      detail: "pedidos que ainda não têm o custo ou a taxa (não foram ao fornecedor, por exemplo)",
    });
  }
  lines.push(
    { label: "Custo dos produtos", cents: -margins.product_cost_cents },
    { label: "Frete ao fornecedor", cents: -margins.freight_cents },
    { label: "Taxas do pagamento", cents: -margins.fee_cents },
  );
  return { lines, left: margins.margin_cents };
}

export type Bucket = {
  label: string;
  from: string;
  to: string;
  gross: number;
  refunds: number;
  orders: number;
};

const short = new Intl.DateTimeFormat("pt-BR", { day: "numeric", month: "short", timeZone: "UTC" });
const fmtDay = (iso: string) => short.format(new Date(`${iso}T00:00:00Z`));

/** Um ponto por dia; acima de 92 dias, por semana (uma coluna por dia viraria um fio). */
export function buckets(daily: Summary["daily"]): { unit: "dia" | "semana"; items: Bucket[] } {
  if (daily.length <= 92) {
    return {
      unit: "dia",
      items: daily.map((d) => ({
        label: fmtDay(d.date),
        from: d.date,
        to: d.date,
        gross: d.gross_sales.amount_cents,
        refunds: d.refunds.amount_cents,
        orders: d.paid_orders,
      })),
    };
  }
  const items: Bucket[] = [];
  for (let i = 0; i < daily.length; i += 7) {
    const week = daily.slice(i, i + 7);
    const first = week[0]!.date;
    const last = week[week.length - 1]!.date;
    items.push({
      label: `${fmtDay(first)} a ${fmtDay(last)}`,
      from: first,
      to: last,
      gross: week.reduce((s, d) => s + d.gross_sales.amount_cents, 0),
      refunds: week.reduce((s, d) => s + d.refunds.amount_cents, 0),
      orders: week.reduce((s, d) => s + d.paid_orders, 0),
    });
  }
  return { unit: "semana", items };
}

/** Marcas redondas para o eixo: 1, 2, 2,5 ou 5 vezes uma potência de 10, umas 5 linhas. */
export function niceTicks(max: number): number[] {
  if (max <= 0) return [0];
  const raw = max / 4;
  const pow = 10 ** Math.floor(Math.log10(raw));
  const step = [1, 2, 2.5, 5, 10].map((m) => m * pow).find((s) => s >= raw)!;
  const ticks: number[] = [];
  for (let t = 0; t <= max + step * 0.0001; t += step) ticks.push(t);
  if (ticks[ticks.length - 1]! < max) ticks.push(ticks[ticks.length - 1]! + step);
  return ticks;
}

/** Os atalhos da visão geral: os últimos 7, 30 e 90 dias e 12 meses, contando hoje. */
export function overviewPresets(now = Date.now()) {
  const today = dayOf(now);
  return [
    { label: "7 dias", days: 7 },
    { label: "30 dias", days: 30 },
    { label: "90 dias", days: 90 },
    { label: "12 meses", days: 365 },
  ].map((p) => ({ label: p.label, de: dayOf(now - (p.days - 1) * DAY), ate: today }));
}
