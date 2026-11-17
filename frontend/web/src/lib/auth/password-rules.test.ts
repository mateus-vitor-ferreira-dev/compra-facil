import { newPasswordProblems, passwordProblem } from "./password-rules";

describe("passwordProblem", () => {
  it("aceita de 8 a 128 caracteres com pelo menos 4 diferentes, sem regras de composição", () => {
    expect(passwordProblem("uma frase longa")).toBeUndefined();
    expect(passwordProblem("abcd".repeat(32))).toBeUndefined();
  });

  it("recusa curta, longa demais ou repetitiva, com o motivo em português", () => {
    expect(passwordProblem("curta")).toBe("A senha precisa de pelo menos 8 caracteres.");
    expect(passwordProblem("a".repeat(129))).toBe("Use até 128 caracteres.");
    expect(passwordProblem("aaaabbbb")).toBe("Use pelo menos 4 caracteres diferentes.");
  });
});

describe("newPasswordProblems", () => {
  it("a senha primeiro; a repetição só depois que a senha vale", () => {
    expect(newPasswordProblems("curta", "outra")).toEqual({
      password: "A senha precisa de pelo menos 8 caracteres.",
    });
    expect(newPasswordProblems("senha-nova-1", "senha-nova-2")).toEqual({
      confirm: "As duas senhas não são iguais.",
    });
    expect(newPasswordProblems("senha-nova-1", "senha-nova-1")).toEqual({});
  });
});
