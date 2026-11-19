/**
 * Os códigos do segundo fator como a pessoa os digita: o app autenticador mostra "123 456" e o código de
 * recuperação vem como "k7m2p-9xq4r". Aqui só o formato; quem confere é o identity.
 */

/** Os 6 dígitos do app autenticador, sem espaço nem hífen; nulo se não forem 6 dígitos. */
export function normalizeOtp(value: unknown): string | null {
  if (typeof value !== "string") return null;
  const digits = value.replace(/[\s-]/g, "");
  return /^[0-9]{6}$/.test(digits) ? digits : null;
}

/** Um código de recuperação: 10 letras e números (o hífen e os espaços são opcionais); nulo se não for. */
export function normalizeRecoveryCode(value: unknown): string | null {
  if (typeof value !== "string" || value.length > 32) return null;
  const clean = value.replace(/[\s-]/g, "").toLowerCase();
  return /^[0-9a-z]{10}$/.test(clean) ? `${clean.slice(0, 5)}-${clean.slice(5)}` : null;
}
