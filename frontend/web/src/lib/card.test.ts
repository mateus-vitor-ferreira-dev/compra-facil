import {
  brandOf,
  caretAfterDigits,
  checkCard,
  formatCardNumber,
  formatExpiry,
  installmentOptions,
  luhn,
} from "./card";

const NOW = new Date("2026-10-29T12:00:00Z");
const good = { number: "4509 9535 6623 3704", name: "APRO", expiry: "11/30", cvv: "123" };

describe("formatação enquanto se digita", () => {
  it("agrupa o número como está impresso, e o Amex em 4-6-5", () => {
    expect(formatCardNumber("4509953566233704")).toBe("4509 9535 6623 3704");
    expect(formatCardNumber("4509 95")).toBe("4509 95");
    expect(formatCardNumber("371180303257522")).toBe("3711 803032 57522");
    // o que passa do tamanho da bandeira fica de fora
    expect(formatCardNumber("45099535662337041234")).toBe("4509 9535 6623 3704");
  });

  it("validade ganha a barra depois do mês, e um dígito acima de 1 já é o mês", () => {
    expect(formatExpiry("1")).toBe("1");
    expect(formatExpiry("4")).toBe("04");
    expect(formatExpiry("113")).toBe("11/3");
    expect(formatExpiry("11/30")).toBe("11/30");
  });

  it("o cursor volta para depois do mesmo dígito", () => {
    expect(caretAfterDigits("4509 9535", 4)).toBe(4);
    expect(caretAfterDigits("4509 9535", 5)).toBe(6);
    expect(caretAfterDigits("4509 9535", 0)).toBe(0);
  });
});

describe("checkCard", () => {
  it("cartão certo: os dados que o provedor recebe", () => {
    expect(checkCard(good, NOW)).toEqual({
      ok: true,
      card: { number: "4509953566233704", name: "APRO", month: 11, year: 2030, cvv: "123", brand: "visa" },
    });
  });

  it("cada erro no seu campo, dizendo o que corrigir", () => {
    const r = checkCard({ number: "6011 1111 1111 1117", name: " ", expiry: "13/30", cvv: "12" }, NOW);
    expect(r.ok).toBe(false);
    if (r.ok) return;
    expect(r.errors.number).toContain("Visa, Mastercard e American Express");
    expect(r.errors.name).toContain("impresso no cartão");
    expect(r.errors.expiry).toContain("MM/AA");
    expect(r.errors.cvv).toContain("3 dígitos");
  });

  it("dígito errado, cartão vencido e o CVV de 4 dígitos do Amex", () => {
    const wrong = checkCard({ ...good, number: "4509 9535 6623 3705" }, NOW);
    expect(!wrong.ok && wrong.errors.number).toContain("algum dígito");
    const old = checkCard({ ...good, expiry: "09/26" }, NOW);
    expect(!old.ok && old.errors.expiry).toBe("Este cartão já venceu.");
    expect(checkCard({ ...good, expiry: "10/26" }, NOW).ok).toBe(true); // vale até o fim do mês
    const amex = checkCard({ ...good, number: "3711 803032 57522", cvv: "123" }, NOW);
    expect(!amex.ok && amex.errors.cvv).toContain("4 dígitos");
  });

  it("bandeira e Luhn como o provedor", () => {
    expect(brandOf("5031")).toBe("master");
    expect(brandOf("34")).toBe("amex");
    expect(brandOf("6011")).toBeUndefined();
    expect(luhn("4509953566233704")).toBe(true);
    expect(luhn("")).toBe(false);
  });
});

describe("installmentOptions", () => {
  it("até 6× sem juros", () => {
    const options = installmentOptions(263_992);
    expect(options).toHaveLength(6);
    expect(options[0]).toEqual({ count: 1, eachCents: 263_992 });
    expect(options[2]).toEqual({ count: 3, eachCents: 87_997 });
  });

  it("sem parcela menor que R$ 30,00", () => {
    expect(installmentOptions(9_000).map((o) => o.count)).toEqual([1, 2, 3]);
    expect(installmentOptions(8_999).map((o) => o.count)).toEqual([1, 2]);
    expect(installmentOptions(5_999)).toEqual([{ count: 1, eachCents: 5_999 }]);
  });
});
