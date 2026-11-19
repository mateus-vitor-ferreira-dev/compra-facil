import { MASKED_EMAIL, maskEmail } from "./guest";

describe("maskEmail", () => {
  it("mostra as duas primeiras letras e o domínio, sem o tamanho do resto", () => {
    expect(maskEmail("convidada@exemplo.com")).toBe("co***@exemplo.com");
    expect(maskEmail("ab@exemplo.com")).toBe("a***@exemplo.com");
    expect(maskEmail("a@exemplo.com")).toBe("a***@exemplo.com");
  });

  it("o mascarado passa no formato que a página aceita da URL; o e-mail inteiro e um texto qualquer, não", () => {
    expect(MASKED_EMAIL.test(maskEmail("convidada@exemplo.com"))).toBe(true);
    expect(MASKED_EMAIL.test("convidada@exemplo.com")).toBe(false);
    expect(MASKED_EMAIL.test("clique aqui***@golpe.com")).toBe(false);
  });
});
