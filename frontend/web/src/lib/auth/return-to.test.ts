import { loginHref, safeReturnTo } from "./return-to";

describe("safeReturnTo", () => {
  it("aceita caminhos da loja, com query", () => {
    expect(safeReturnTo("/carrinho")).toBe("/carrinho");
    expect(safeReturnTo("/conta/pedidos/1?x=1")).toBe("/conta/pedidos/1?x=1");
  });

  it("recusa outro site, caminho relativo e as telas de entrada", () => {
    for (const bad of [
      "//evil.example",
      "/\\evil.example",
      "https://evil.example",
      "conta",
      "",
      null,
      "/entrar?voltar=/x",
      "/cadastro",
    ]) {
      expect(safeReturnTo(bad)).toBe("/conta");
    }
    expect(safeReturnTo("/" + "a".repeat(600), "/")).toBe("/");
  });

  it("loginHref codifica o destino", () => {
    expect(loginHref("/conta/pedidos?status=pending")).toBe(
      "/entrar?voltar=%2Fconta%2Fpedidos%3Fstatus%3Dpending",
    );
  });
});
