import { describe, expect, it } from "vitest";
import { buckets, niceTicks, overviewPeriod, receipt } from "./sales-overview";

const money = (cents: number) => ({ amount_cents: cents, currency: "BRL" });
const day = (date: string, gross: number, refunds = 0, paid = 1) => ({
  date,
  paid_orders: paid,
  gross_sales: money(gross),
  refunds: money(refunds),
});

const summary = (gross: number, refunds: number) =>
  ({
    from: "2026-10-06T03:00:00Z",
    to: "2026-11-05T03:00:00Z",
    orders: { placed: 12, paid: 10, refunded: 2, not_paid: 2 },
    gross_sales: money(gross),
    refunds: money(refunds),
    net_sales: money(gross - refunds),
    average_ticket: money(gross / 10),
    shipping_charged: money(1500),
    daily: [],
    by_payment_method: [],
    top_products: [],
  }) as Parameters<typeof receipt>[0];

const sum = (lines: { cents: number }[]) => lines.reduce((s, l) => s + l.cents, 0);

describe("visão geral", () => {
  it("sem período na URL, os últimos 30 dias de Brasília", () => {
    const now = Date.parse("2026-11-04T15:00:00Z");
    expect(overviewPeriod({}, now)).toEqual({
      de: "2026-10-06",
      ate: "2026-11-04",
      from: "2026-10-06T03:00:00.000Z",
      to: "2026-11-05T03:00:00.000Z",
    });
  });

  it("período de mais de um ano, invertido ou pela metade mostra os 30 dias e avisa", () => {
    const now = Date.parse("2026-11-04T15:00:00Z");
    for (const q of [
      { de: "2025-01-01", ate: "2026-11-04" },
      { de: "2026-11-04", ate: "2026-11-01" },
      { de: "2026-11-01" },
    ]) {
      const p = overviewPeriod(q, now);
      expect(p.error, JSON.stringify(q)).toBeTruthy();
      expect(p.from).toBe("2026-10-06T03:00:00.000Z");
      expect(p.de).toBe(q.de); // o campo mostra o que a pessoa digitou
    }
  });

  it("o extrato fecha no que sobrou, com a linha do que ainda não tem a conta", () => {
    const margins = {
      orders: 10,
      counted: 6,
      refunded: 2,
      incomplete: 2,
      revenue_cents: 60000,
      product_cost_cents: 30000,
      freight_cents: 8000,
      fee_cents: 3000,
      margin_cents: 19000,
    };
    const { lines, left } = receipt(summary(100000, 20000), margins);
    expect(lines.map((l) => l.label)).toEqual([
      "Vendas brutas",
      "Estornos",
      "Ainda sem a conta fechada",
      "Custo dos produtos",
      "Frete ao fornecedor",
      "Taxas do pagamento",
    ]);
    expect(lines.find((l) => l.label === "Ainda sem a conta fechada")?.cents).toBe(-20000);
    expect(sum(lines)).toBe(left);
    expect(left).toBe(19000);
  });

  it("com tudo fechado, a linha do que falta não aparece; sem as margens, sobra o líquido", () => {
    const closed = {
      orders: 10,
      counted: 8,
      refunded: 2,
      incomplete: 0,
      revenue_cents: 80000,
      product_cost_cents: 40000,
      freight_cents: 0,
      fee_cents: 0,
      margin_cents: 40000,
    };
    expect(receipt(summary(100000, 20000), closed).lines.some((l) => l.label.startsWith("Ainda"))).toBe(
      false,
    );
    const { lines, left } = receipt(summary(100000, 20000), null);
    expect(lines).toHaveLength(2);
    expect(left).toBe(80000);
  });

  it("até 92 dias, uma coluna por dia; acima, por semana, somando", () => {
    const days = Array.from({ length: 100 }, (_, i) =>
      day(new Date(Date.UTC(2026, 0, 1 + i)).toISOString().slice(0, 10), 1000, i === 0 ? 500 : 0),
    );
    expect(buckets(days.slice(0, 30)).unit).toBe("dia");
    const weeks = buckets(days);
    expect(weeks.unit).toBe("semana");
    expect(weeks.items).toHaveLength(15);
    expect(weeks.items[0]).toMatchObject({
      from: "2026-01-01",
      to: "2026-01-07",
      gross: 7000,
      refunds: 500,
      orders: 7,
    });
    expect(weeks.items.at(-1)).toMatchObject({ from: "2026-04-09", to: "2026-04-10", gross: 2000 });
  });

  it("o eixo tem marcas redondas que cobrem o maior valor", () => {
    expect(niceTicks(0)).toEqual([0]);
    expect(niceTicks(9500)).toEqual([0, 2500, 5000, 7500, 10000]);
    expect(niceTicks(100)).toEqual([0, 25, 50, 75, 100]);
    expect(niceTicks(1234)).toEqual([0, 500, 1000, 1500]);
  });
});
