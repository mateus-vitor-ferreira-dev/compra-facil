/**
 * A regra de margem do catálogo do fornecedor (#217), a mesma do catalog (domain.SalePriceCents): o
 * custo no câmbio, mais a margem, arredondado para cima até terminar em ,90, nunca abaixo do mínimo.
 * Serve para o painel mostrar a conta enquanto a equipe digita; quem grava o preço é o catalog.
 */

/** "5.40" (reais por unidade) em milésimos de real: 5400. */
export function parseRate(rate: string): number | null {
  const m = /^(\d+)\.(\d{1,3})$/.exec(rate.trim());
  if (!m) return null;
  const milli = Number(m[1]) * 1000 + Number(m[2]!.padEnd(3, "0"));
  return milli > 0 ? milli : null;
}

const ceilDiv = (a: number, b: number) => (a <= 0 ? Math.trunc(a / b) : Math.floor((a + b - 1) / b));

/** O menor valor >= cents que termina em ,90. */
export const roundUpTo90 = (cents: number) => Math.max(ceilDiv(cents - 90, 100), 0) * 100 + 90;

export type PriceBreakdown = {
  /** o custo já em reais */
  costBrl: number;
  /** com a margem, antes do arredondamento */
  withMargin: number;
  /** o mínimo da regra pesou */
  minApplied: boolean;
  price: number;
};

export function salePrice(
  costCents: number,
  rateMilli: number,
  marginPercent: number,
  minPriceCents: number,
): PriceBreakdown {
  const costBrl = ceilDiv(costCents * rateMilli, 1000);
  const withMargin = ceilDiv(costBrl * (100 + marginPercent), 100);
  return {
    costBrl,
    withMargin,
    minApplied: minPriceCents > withMargin,
    price: roundUpTo90(Math.max(withMargin, minPriceCents)),
  };
}
