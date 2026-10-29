/** Quantos lotes ("Carregar mais") a listagem tem, sem passar do máximo de páginas que a API aceita. */
export const pageCount = (total: number, perPage: number, max = 200) =>
  Math.min(Math.max(Math.ceil(total / perPage), 1), max);
