import { describe, expect, it } from "vitest";
import { addItem, emptyCart, lineKey, removeItem } from "./cart-rules";
import { normalizePersonalization, personalizationError, type PersonalizationRule } from "./personalization";

const rule: PersonalizationRule = {
  label: "Nome",
  max_length: 12,
  required: true,
  template: "nome-classico",
};

describe("personalização: as regras do catalog", () => {
  it("normaliza os espaços", () => {
    expect(normalizePersonalization("  Ana \t  Maria \n")).toBe("Ana Maria");
    expect(normalizePersonalization("   ")).toBe("");
  });

  it.each([
    ["Sr. D'Ávila", undefined],
    ["Desde 2019!", undefined],
    ["Çéü-ñ/?,& 12", undefined],
    ["", "Escreva o nome."],
    ["Ana Carolina S", "Nome: no máximo 12 caracteres."],
    ["Bia 🎄", "Nome: use letras, números, espaço e . , ' - & ! ? / (sem emoji)."],
    ["Анна", "Nome: use letras, números, espaço e . , ' - & ! ? / (sem emoji)."],
    ["a#b", "Nome: use letras, números, espaço e . , ' - & ! ? / (sem emoji)."],
  ])("%j", (text, error) => {
    expect(personalizationError(text, rule)).toBe(error);
  });

  it("opcional aceita vazio; o produto sem personalização recusa texto", () => {
    expect(personalizationError("", { ...rule, required: false })).toBeUndefined();
    expect(personalizationError("", null)).toBeUndefined();
    expect(personalizationError("Ana", null)).toBe("Este produto não tem personalização.");
  });
});

describe("personalização no carrinho", () => {
  it("cada texto é uma linha; o mesmo texto soma", () => {
    let cart = emptyCart();
    for (const text of ["Ana", "João", "Ana"]) {
      const r = addItem(cart, "p1", 1, 7990, undefined, "M", text);
      if (!r.ok) throw new Error("não entrou");
      cart = r.cart;
    }
    expect(cart.items).toEqual([
      { id: "p1", qty: 2, seen: 7990, size: "M", text: "Ana" },
      { id: "p1", qty: 1, seen: 7990, size: "M", text: "João" },
    ]);
    expect(lineKey(cart.items[1]!)).toBe("p1:M|João");
    expect(removeItem(cart, "p1:M|Ana").items).toHaveLength(1);
  });

  it("sem tamanho, a chave é o id e o texto", () => {
    expect(lineKey({ id: "p1", text: "Ana" })).toBe("p1|Ana");
    expect(lineKey({ id: "p1" })).toBe("p1");
  });
});

describe("cor no carrinho", () => {
  it("cada cor é uma linha, e a chave junta tamanho, cor e texto", () => {
    let cart = emptyCart();
    for (const color of ["Preto", "Branco", "preto"]) {
      const r = addItem(cart, "p1", 1, 7990, undefined, "M", undefined, color);
      if (!r.ok) throw new Error("não entrou");
      cart = r.cart;
    }
    expect(cart.items.map((i) => [i.color, i.qty])).toEqual([
      ["preto", 2],
      ["Branco", 1],
    ]);
    expect(lineKey({ id: "p1", size: "M", color: "Preto", text: "Ana" })).toBe("p1:M~PRETO|Ana");
  });
});
