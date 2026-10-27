import { routePattern } from "./route-pattern";

describe("routePattern", () => {
  it("troca ids e slugs pelo padrão da rota", () => {
    expect(routePattern("/produto/0199f6a0-0000-7000-8000-000000000000")).toBe("/produto/[id]");
    expect(routePattern("/categoria/fones?marca=JBL")).toBe("/categoria/[slug]");
    expect(routePattern("/conta/pedidos/abc")).toBe("/conta/pedidos/[id]");
    expect(routePattern("/admin/produtos/novo")).toBe("/admin/produtos/novo");
    expect(routePattern("/admin/produtos/123")).toBe("/admin/produtos/[id]");
  });

  it("mantém as páginas conhecidas e junta o resto", () => {
    expect(routePattern("/")).toBe("/");
    expect(routePattern("/carrinho/")).toBe("/carrinho");
    expect(routePattern("/qualquer/coisa")).toBe("outra");
  });
});
