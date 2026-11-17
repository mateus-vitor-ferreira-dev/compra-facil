/**
 * A regra do nome no identity (cadastro e compra sem conta): o nome vai na saudação dos e-mails da loja,
 * então só letras (com acento), espaços, apóstrofo, hífen e ponto, sem nada com cara de link, e-mail ou
 * domínio ("exemplo.com", "www."). Serve ao navegador (aviso antes de enviar) e à server action; o
 * identity confere de novo e é quem decide.
 */
export const PERSON_NAME_MESSAGE = "Use só letras, espaços, apóstrofo, hífen e ponto.";

const ALLOWED = /^(?=.*\p{L})[\p{L}\p{M} '’.-]+$/u;
// letra, ponto e 2+ letras ("evil.com") ou "www."; iniciais como "J. R." passam
const DOMAIN_LIKE = /\p{L}\.\p{L}{2,}|(^|\s)www\./iu;

/** Sem espaços nas pontas e com um só entre as palavras, como o identity guarda. */
export function normalizePersonName(name: string): string {
  return name.replace(/\s+/gu, " ").trim();
}

/** O nome passa na regra do identity (o tamanho fica com quem chama). */
export function isPersonName(name: string): boolean {
  const normalized = normalizePersonName(name);
  return ALLOWED.test(normalized) && !DOMAIN_LIKE.test(normalized);
}
