/**
 * O cartão digitado no checkout (#220): formatação enquanto se digita, a conferência antes de mandar ao
 * provedor e as parcelas. Roda no navegador; nada daqui vai para o servidor da loja.
 */

export type Brand = "visa" | "master" | "amex";

export const BRAND_NAMES: Record<Brand, string> = {
  visa: "Visa",
  master: "Mastercard",
  amex: "American Express",
};

export const digitsOf = (value: string) => value.replace(/\D/g, "");

/** A bandeira pelo começo do número, como o provedor a reconhece. */
export function brandOf(digits: string): Brand | undefined {
  if (digits.startsWith("4")) return "visa";
  if (digits.startsWith("5")) return "master";
  if (digits.startsWith("34") || digits.startsWith("37")) return "amex";
  return undefined;
}

const maxDigits = (brand?: Brand) => (brand === "amex" ? 15 : 16);
export const cvvLength = (brand?: Brand) => (brand === "amex" ? 4 : 3);

/** "4509 9535 6623 3704", ou "3711 803032 57522" no Amex, como está impresso no cartão. */
export function formatCardNumber(value: string): string {
  const digits = digitsOf(value);
  const brand = brandOf(digits);
  const kept = digits.slice(0, maxDigits(brand));
  const groups = brand === "amex" ? [4, 6, 5] : [4, 4, 4, 4];
  const parts: string[] = [];
  let at = 0;
  for (const size of groups) {
    if (at >= kept.length) break;
    parts.push(kept.slice(at, at + size));
    at += size;
  }
  return parts.join(" ");
}

/** "11/30" enquanto se digita: a barra entra sozinha depois do mês. */
export function formatExpiry(value: string): string {
  let digits = digitsOf(value).slice(0, 4);
  // "4" já é abril: vira "04" para o cliente seguir direto para o ano
  if (digits.length === 1 && Number(digits) > 1) digits = `0${digits}`;
  return digits.length > 2 ? `${digits.slice(0, 2)}/${digits.slice(2)}` : digits;
}

/** Onde fica o cursor depois de reformatar: logo depois do mesmo número de dígitos que havia antes dele. */
export function caretAfterDigits(formatted: string, digitsBefore: number): number {
  if (digitsBefore <= 0) return 0;
  let seen = 0;
  for (let i = 0; i < formatted.length; i++) {
    if (/\d/.test(formatted[i] ?? "")) seen++;
    if (seen === digitsBefore) return i + 1;
  }
  return formatted.length;
}

export function luhn(digits: string): boolean {
  let sum = 0;
  for (let i = 0; i < digits.length; i++) {
    let d = Number(digits[digits.length - 1 - i]);
    if (i % 2 === 1) {
      d *= 2;
      if (d > 9) d -= 9;
    }
    sum += d;
  }
  return digits.length > 0 && sum % 10 === 0;
}

export type CardInput = { number: string; name: string; expiry: string; cvv: string };
export type CardField = keyof CardInput;
export type CardErrors = Partial<Record<CardField, string>>;
export type CheckedCard = {
  number: string;
  name: string;
  month: number;
  year: number;
  cvv: string;
  brand: Brand;
};

/** Confere o cartão antes de mandar ao provedor: cada erro diz o que corrigir, no campo dele. */
export function checkCard(
  input: CardInput,
  now = new Date(),
): { ok: true; card: CheckedCard } | { ok: false; errors: CardErrors } {
  const errors: CardErrors = {};
  const number = digitsOf(input.number);
  const brand = brandOf(number);
  if (number.length === 0) errors.number = "Digite o número do cartão.";
  else if (!brand) errors.number = "Aceitamos Visa, Mastercard e American Express.";
  else if (number.length !== maxDigits(brand) || !luhn(number))
    errors.number = "Confira o número: algum dígito está errado ou faltando.";

  const name = input.name.trim().replace(/\s+/g, " ");
  if (name.length < 2) errors.name = "Digite o nome como está impresso no cartão.";

  const expiry = /^(\d{2})\/(\d{2})$/.exec(input.expiry.trim());
  const month = Number(expiry?.[1]);
  const year = 2000 + Number(expiry?.[2]);
  if (!expiry || month < 1 || month > 12) errors.expiry = "Use o formato MM/AA, como em 11/30.";
  else if (year < now.getFullYear() || (year === now.getFullYear() && month < now.getMonth() + 1))
    errors.expiry = "Este cartão já venceu.";

  const cvv = digitsOf(input.cvv);
  if (cvv.length !== cvvLength(brand))
    errors.cvv =
      brand === "amex" ? "São os 4 dígitos na frente do cartão." : "São os 3 dígitos no verso do cartão.";

  if (Object.keys(errors).length > 0 || !brand) return { ok: false, errors };
  return { ok: true, card: { number, name, month, year, cvv, brand } };
}

/** A menor parcela que a loja aceita (em centavos): cada parcela a mais custa taxa, e a venda pequena não a paga. */
export const MIN_INSTALLMENT_CENTS = 3000;
export const MAX_INSTALLMENTS = 6;

/** As parcelas que o total permite, sem juros: de 1× até 6×, sem parcela menor que R$ 30,00 (o orders confere de novo). */
export function installmentOptions(totalCents: number): { count: number; eachCents: number }[] {
  const options = [{ count: 1, eachCents: totalCents }];
  for (let count = 2; count <= MAX_INSTALLMENTS; count++) {
    const eachCents = Math.round(totalCents / count);
    if (Math.floor(totalCents / count) < MIN_INSTALLMENT_CENTS) break;
    options.push({ count, eachCents });
  }
  return options;
}
