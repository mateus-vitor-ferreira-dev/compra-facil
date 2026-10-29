import { parseRate, roundUpTo90, salePrice } from "./pricing";

describe("salePrice", () => {
  it("faz a mesma conta do catalog", () => {
    // US$ 12,40 × 5,40 = R$ 66,96; +80% = R$ 120,53; arredondado R$ 120,90
    expect(salePrice(1240, 5400, 80, 1990)).toEqual({
      costBrl: 6696,
      withMargin: 12053,
      minApplied: false,
      price: 12090,
    });
    // barato: vale o mínimo, também terminando em ,90
    expect(salePrice(190, 5400, 50, 2000)).toMatchObject({ minApplied: true, price: 2090 });
  });

  it("arredonda para o próximo ,90", () => {
    expect([1, 90, 91, 6696, 6790, 6791].map(roundUpTo90)).toEqual([90, 90, 190, 6790, 6790, 6890]);
  });

  it("lê o câmbio da API", () => {
    expect(parseRate("5.40")).toBe(5400);
    expect(parseRate("0.183")).toBe(183);
    expect(parseRate("abc")).toBeNull();
    expect(parseRate("0.00")).toBeNull();
  });
});
