import { parseReais, reaisInput } from "./money-input";

describe("parseReais", () => {
  it("aceita o jeito brasileiro e o com ponto decimal", () => {
    expect(parseReais("1.299,90")).toBe(129990);
    expect(parseReais("1299,9")).toBe(129990);
    expect(parseReais("1299.90")).toBe(129990);
    expect(parseReais("R$ 49")).toBe(4900);
    expect(parseReais("1.000.000")).toBe(100000000);
  });

  it("recusa o que não é valor", () => {
    expect(parseReais("")).toBeNull();
    expect(parseReais("12,345")).toBeNull();
    expect(parseReais("abc")).toBeNull();
    expect(parseReais("-5")).toBeNull();
  });

  it("volta ao formato do campo", () => {
    expect(reaisInput(129990)).toBe("1299,90");
  });
});
