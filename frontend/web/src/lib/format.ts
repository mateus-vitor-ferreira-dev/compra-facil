type Money = { amount_cents: number; currency?: string };

const brl = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" });

/** R$ 1.234,56, a partir do Money dos contratos (centavos). */
export function formatMoney(money: Money): string {
  if (money.currency && money.currency !== "BRL") {
    return new Intl.NumberFormat("pt-BR", { style: "currency", currency: money.currency }).format(
      money.amount_cents / 100,
    );
  }
  return brl.format(money.amount_cents / 100);
}

/** Reais e centavos separados, para a etiqueta: { reais: "2.639", centavos: "90" }. */
export function splitMoney(money: Money): { symbol: string; reais: string; centavos: string } {
  const parts = new Intl.NumberFormat("pt-BR", {
    style: "currency",
    currency: money.currency ?? "BRL",
  }).formatToParts(money.amount_cents / 100);
  const join = (types: string[]) =>
    parts
      .filter((p) => types.includes(p.type))
      .map((p) => p.value)
      .join("");
  return { symbol: join(["currency"]), reais: join(["integer", "group"]), centavos: join(["fraction"]) };
}

/** Desconto em % entre o de tabela e o efetivo, arredondado para baixo (como o catálogo calcula). */
export function discountPercent(price: Money, effective: Money): number {
  if (price.amount_cents <= 0 || effective.amount_cents >= price.amount_cents) return 0;
  return Math.floor(((price.amount_cents - effective.amount_cents) * 100) / price.amount_cents);
}
