/**
 * O caminho da página como padrão de rota ("/produto/[id]"), para as métricas do front: um label por
 * página, não um por produto ou pedido (o Prometheus não aguenta uma série por id).
 */
const PATTERNS: [RegExp, string][] = [
  [/^\/produto\/[^/]+$/, "/produto/[id]"],
  [/^\/categoria\/[^/]+$/, "/categoria/[slug]"],
  [/^\/conta\/pedidos\/[^/]+$/, "/conta/pedidos/[id]"],
  [/^\/admin\/produtos\/(?!novo$)[^/]+$/, "/admin/produtos/[id]"],
  [/^\/admin\/campanhas\/(?!nova$)[^/]+$/, "/admin/campanhas/[id]"],
];

const KNOWN = new Set([
  "/",
  "/ofertas",
  "/busca",
  "/carrinho",
  "/checkout",
  "/entrar",
  "/cadastro",
  "/conta",
  "/conta/confirmar-email",
  "/admin/produtos",
  "/admin/produtos/novo",
  "/admin/categorias",
  "/admin/campanhas",
  "/admin/campanhas/nova",
  "/admin/falhas",
]);

export function routePattern(path: string): string {
  const clean = path.split(/[?#]/)[0]!.replace(/\/+$/, "") || "/";
  if (KNOWN.has(clean)) return clean;
  return PATTERNS.find(([re]) => re.test(clean))?.[1] ?? "outra";
}
