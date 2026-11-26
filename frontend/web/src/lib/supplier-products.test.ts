import { describe, expect, it } from "vitest";
import {
  formatSupplierProducts,
  hasSupplierProducts,
  madePerOrder,
  parseSupplierProducts,
  supplierCodeFor,
} from "./supplier-products";

const bw = ["Branco", "Preto"];

describe("código na Montink: o que o painel escreve", () => {
  it("vazio é sem fornecedor", () => {
    expect(parseSupplierProducts("  ", bw)).toEqual({ ok: true, value: {} });
  });

  it("um código só vale para todas as cores", () => {
    expect(parseSupplierProducts(" 6167609 ", bw)).toEqual({
      ok: true,
      value: { Branco: "6167609", Preto: "6167609" },
    });
  });

  it("no produto sem cor, a chave é vazia", () => {
    expect(parseSupplierProducts("6168525", [])).toEqual({ ok: true, value: { "": "6168525" } });
  });

  it("por cor, quando o Vermelho é outro produto na Montink (sem diferenciar maiúsculas)", () => {
    expect(
      parseSupplierProducts("branco: 6167609, PRETO: 6167609\nVermelho: 6170123", [...bw, "Vermelho"]),
    ).toEqual({ ok: true, value: { Branco: "6167609", Preto: "6167609", Vermelho: "6170123" } });
  });

  it.each([
    ["Branco: 6167609", bw, "Falta o código de Preto"],
    ["Branco: 6167609, Azul: 1", bw, "A cor “Azul” não está nas cores do produto."],
    ["Branco: 1, Branco: 2, Preto: 3", bw, "A cor Branco aparece mais de uma vez."],
    ["Branco:, Preto: 3", bw, "Escreva a cor e o código assim: Branco: 6167609."],
    ["61 67", bw, "O código tem só letras, dígitos e hífen"],
    ["Branco: 1", [], "O produto não tem cor à escolha"],
  ])("%s com %j recusa: %s", (text, colors, error) => {
    const got = parseSupplierProducts(text, colors);
    expect(got.ok).toBe(false);
    if (!got.ok) expect(got.error).toContain(error);
  });
});

describe("código na Montink: o que o painel mostra", () => {
  it("um código só quando todas as cores usam o mesmo produto", () => {
    expect(formatSupplierProducts({ Branco: "6167609", Preto: "6167609" }, bw)).toBe("6167609");
    expect(formatSupplierProducts({ "": "6168525" }, [])).toBe("6168525");
  });

  it("por cor, na ordem das cores, quando há mais de um", () => {
    expect(
      formatSupplierProducts({ Vermelho: "6170123", Preto: "6167609", Branco: "6167609" }, [
        ...bw,
        "Vermelho",
      ]),
    ).toBe("Branco: 6167609, Preto: 6167609, Vermelho: 6170123");
  });

  it("vazio sem fornecedor, e ida e volta dá o mesmo", () => {
    expect(formatSupplierProducts({}, bw)).toBe("");
    expect(formatSupplierProducts(undefined, bw)).toBe("");
    const value = { Branco: "1", Preto: "2" };
    const back = parseSupplierProducts(formatSupplierProducts(value, bw), bw);
    expect(back).toEqual({ ok: true, value });
  });

  it("tem fornecedor?", () => {
    expect(hasSupplierProducts({ "": "1" })).toBe(true);
    expect(hasSupplierProducts({})).toBe(false);
    expect(hasSupplierProducts(undefined)).toBe(false);
  });
});

describe("código na Montink: na ficha do pedido", () => {
  it("o código da cor do item, sem diferenciar maiúsculas; o único no produto sem cor", () => {
    const codes = { Branco: "6167609", Vermelho: "6170123" };
    expect(supplierCodeFor(codes, "vermelho")).toBe("6170123");
    expect(supplierCodeFor(codes, "Preto")).toBeUndefined();
    expect(supplierCodeFor({ "": "6168525" }, null)).toBe("6168525");
    expect(supplierCodeFor(undefined, "Branco")).toBeUndefined();
  });

  it("feito por pedido só com o texto obrigatório", () => {
    expect(madePerOrder({ required: true })).toBe(true);
    expect(madePerOrder({ required: false })).toBe(false);
    expect(madePerOrder(null)).toBe(false);
  });
});
