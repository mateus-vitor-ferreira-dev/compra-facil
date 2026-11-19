import { isPersonName, normalizePersonName } from "./person-name";

describe("isPersonName", () => {
  it.each(["Ana Maria", "João d'Ávila", "João d’Ávila", "Maria-Clara", "J. R. Silva", "Conceição Ñuñez"])(
    "aceita o nome de verdade %s",
    (name) => expect(isPersonName(name)).toBe(true),
  );

  it.each([
    "https://embrulhado-seguranca.com/verificar",
    "embrulhado-seguranca.com",
    "evil.com",
    "Ana evil.com.br",
    "www.x",
    "ana@exemplo.com",
    "a/b",
    "pix: 123",
    "Ana 2",
    ". - '",
    "   ",
  ])("recusa %s", (name) => expect(isPersonName(name)).toBe(false));
});

it("normalizePersonName tira os espaços das pontas e junta os repetidos", () => {
  expect(normalizePersonName("  Ana \t Maria\n Souza ")).toBe("Ana Maria Souza");
});
