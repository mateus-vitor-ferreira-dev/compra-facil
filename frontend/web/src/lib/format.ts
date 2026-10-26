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
