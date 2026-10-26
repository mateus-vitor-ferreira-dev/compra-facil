import { formatMoney } from "./format";

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
