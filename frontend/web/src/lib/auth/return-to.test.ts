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

  it("recusa o caminho que o navegador transforma em outro site", () => {
    // tab, quebra de linha e barra invertida somem ou viram "/" no parser de URL: "/\t/x" vira "//x"
    for (const bad of [
      "/\t/evil.example",
      "/\n/evil.example",
      "/\r/evil.example",
      "/\t\\evil.example",
      "/a\\b",
    ]) {
      expect(safeReturnTo(bad)).toBe("/conta");
      expect(new URL(safeReturnTo(bad), "https://loja.example").origin).toBe("https://loja.example");
    }
  });

  it("normaliza o caminho e mantém query e âncora", () => {
    expect(safeReturnTo("/conta/../carrinho?x=1#y")).toBe("/carrinho?x=1#y");
    expect(safeReturnTo("/./entrar")).toBe("/conta");
  });

  it("loginHref codifica o destino", () => {
    expect(loginHref("/conta/pedidos?status=pending")).toBe(
      "/entrar?voltar=%2Fconta%2Fpedidos%3Fstatus%3Dpending",
    );
  });
});
