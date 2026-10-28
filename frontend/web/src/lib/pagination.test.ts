import { pageCount, pageItems } from "./pagination";

describe("pageItems", () => {
  it("poucas páginas: todas", () => {
    expect(pageItems(1, 1)).toEqual([1]);
    expect(pageItems(2, 4)).toEqual([1, 2, 3, 4]);
  });

  it("muitas páginas: primeira, vizinhas da atual, última e os saltos", () => {
    expect(pageItems(1, 42)).toEqual([1, 2, "gap", 42]);
    expect(pageItems(10, 42)).toEqual([1, "gap", 9, 10, 11, "gap", 42]);
    expect(pageItems(42, 42)).toEqual([1, "gap", 41, 42]);
  });

  it("não esconde uma página só atrás de reticências", () => {
    expect(pageItems(4, 42)).toEqual([1, 2, 3, 4, 5, "gap", 42]);
    expect(pageItems(39, 42)).toEqual([1, "gap", 38, 39, 40, 41, 42]);
  });

  it("sem resultados, sem números", () => {
    expect(pageItems(1, 0)).toEqual([]);
  });
});

describe("pageCount", () => {
  it("arredonda para cima e respeita o máximo da API", () => {
    expect(pageCount(0, 24)).toBe(1);
    expect(pageCount(25, 24)).toBe(2);
    expect(pageCount(10_000, 24)).toBe(200);
  });
});
