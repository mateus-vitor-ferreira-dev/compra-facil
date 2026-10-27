import { discountPercent, formatMoney, splitMoney } from "./format";

describe("formatMoney", () => {
  it.each([
    [0, "R$ 0,00"],
    [5, "R$ 0,05"],
    [218390, "R$ 2.183,90"],
  ])("%i centavos = %s", (cents, expected) => {
    // o Intl usa espaço não separável entre o símbolo e o valor
    expect(formatMoney({ amount_cents: cents }).replace(/\s/g, " ")).toBe(expected);
  });
});

describe("splitMoney e discountPercent", () => {
  it("separa reais e centavos para a etiqueta", () => {
    const { symbol, reais, centavos } = splitMoney({ amount_cents: 263990, currency: "BRL" });
    expect([symbol, reais, centavos]).toEqual(["R$", "2.639", "90"]);
  });

  it("desconto arredonda para baixo, como o catálogo", () => {
    expect(
      discountPercent({ amount_cents: 1000, currency: "BRL" }, { amount_cents: 667, currency: "BRL" }),
    ).toBe(33);
    expect(
      discountPercent({ amount_cents: 1000, currency: "BRL" }, { amount_cents: 1000, currency: "BRL" }),
    ).toBe(0);
  });
});
