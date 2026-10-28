/** A cadeia da raiz até a categoria, inclusive: [departamento, subcategoria, tipo] (#210). */
export function categoryPath<T extends { id: string; parent_id?: string | null }>(all: T[], id: string): T[] {
  const byId = new Map(all.map((c) => [c.id, c]));
  const path: T[] = [];
  // o limite protege de um ciclo nos dados
  for (let c = byId.get(id); c && path.length < 8; c = c.parent_id ? byId.get(c.parent_id) : undefined) {
    path.unshift(c);
  }
  return path;
}
