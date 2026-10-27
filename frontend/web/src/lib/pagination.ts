/**
 * Quais números a paginação mostra: a primeira e a última sempre, a atual e as vizinhas, e "…" onde
 * pula. Um "…" que esconderia uma página só vira a própria página (1 2 3, não 1 … 3).
 */
export type PageItem = number | "gap";

export function pageItems(current: number, total: number, around = 1): PageItem[] {
  if (total <= 1) return total === 1 ? [1] : [];
  const pages = new Set([1, total]);
  for (let p = current - around; p <= current + around; p++) if (p >= 1 && p <= total) pages.add(p);
  const sorted = [...pages].sort((a, b) => a - b);
  const out: PageItem[] = [];
  for (const p of sorted) {
    const last = out.at(-1);
    if (typeof last === "number" && p - last === 2) out.push(last + 1);
    else if (typeof last === "number" && p - last > 2) out.push("gap");
    out.push(p);
  }
  return out;
}

/** Quantas páginas a listagem tem, sem passar do máximo que a API aceita. */
export const pageCount = (total: number, perPage: number, max = 200) =>
  Math.min(Math.max(Math.ceil(total / perPage), 1), max);
