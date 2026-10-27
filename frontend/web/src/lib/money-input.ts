/**
 * O preço que a equipe digita, em centavos: "1.299,90", "1299,90", "1299.90", "R$ 49". Nulo se não for
 * um valor com até dois decimais.
 */
export function parseReais(raw: string): number | null {
  const compact = raw.replace(/\s|R\$/g, "");
  // ponto seguido de três dígitos é milhar (1.299); vírgula é o decimal
  const normalized = compact.replace(/\.(?=\d{3}(\D|$))/g, "").replace(",", ".");
  if (!/^\d+(\.\d{1,2})?$/.test(normalized)) return null;
  return Math.round(Number(normalized) * 100);
}

/** Centavos no formato do campo: 129990 → "1299,90". */
export const reaisInput = (cents: number) => (cents / 100).toFixed(2).replace(".", ",");
