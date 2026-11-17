/**
 * As regras da senha do identity (cadastro e senha nova): de 8 a 128 caracteres, com pelo menos 4
 * diferentes, sem regras de composição. Servem ao navegador (aviso antes de enviar) e à server action
 * (sem JavaScript); o identity confere de novo.
 */
export function passwordProblem(password: string): string | undefined {
  if (password.length < 8) return "A senha precisa de pelo menos 8 caracteres.";
  if (password.length > 128) return "Use até 128 caracteres.";
  if (new Set(password).size < 4) return "Use pelo menos 4 caracteres diferentes.";
  return undefined;
}

/** O problema da senha nova e da repetição dela, campo a campo (vazio: pode enviar). */
export function newPasswordProblems(
  password: string,
  confirm: string,
): { password?: string; confirm?: string } {
  const problem = passwordProblem(password);
  if (problem) return { password: problem };
  if (confirm !== password) return { confirm: "As duas senhas não são iguais." };
  return {};
}
