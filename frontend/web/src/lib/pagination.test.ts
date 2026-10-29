import { pageCount } from "./pagination";

describe("pageCount", () => {
  it("arredonda para cima e respeita o máximo da API", () => {
    expect(pageCount(0, 48)).toBe(1);
    expect(pageCount(49, 48)).toBe(2);
    expect(pageCount(100_000, 48)).toBe(200);
  });
});
