/**
 * O frete e o prazo (#222) como o cliente lê: a cotação vem em dias úteis, e a loja mostra as datas em que o
 * pedido chega, contadas de hoje em Brasília, sem os fins de semana (feriados não entram, como no pedido).
 */

export type ShippingOption = {
  id: string;
  name: string;
  price_cents: number;
  min_days: number;
  max_days: number;
};
export type ShippingItem = { sku: string; quantity: number };

const BRASILIA = "America/Sao_Paulo";

/** O dia de hoje em Brasília, como data (meio-dia UTC, para a conta não virar o dia). */
function today(now: Date): Date {
  const [y, m, d] = new Intl.DateTimeFormat("en-CA", { timeZone: BRASILIA })
    .format(now)
    .split("-")
    .map(Number);
  return new Date(Date.UTC(y!, m! - 1, d!, 12));
}

/** A data depois de `days` dias úteis a partir de hoje. */
export function addBusinessDays(days: number, now = new Date()): Date {
  const day = today(now);
  for (let left = days; left > 0;) {
    day.setUTCDate(day.getUTCDate() + 1);
    const weekday = day.getUTCDay();
    if (weekday !== 0 && weekday !== 6) left--;
  }
  return day;
}

const dayMonth = new Intl.DateTimeFormat("pt-BR", { day: "numeric", month: "short", timeZone: "UTC" });
const dayOnly = new Intl.DateTimeFormat("pt-BR", { day: "numeric", timeZone: "UTC" });
const month = new Intl.DateTimeFormat("pt-BR", { month: "short", timeZone: "UTC" });

/** "chega entre 13 e 21 de nov.", ou "entre 28 de nov. e 4 de dez." quando atravessa o mês. */
export function arrival(option: Pick<ShippingOption, "min_days" | "max_days">, now = new Date()): string {
  const from = addBusinessDays(option.min_days, now);
  const to = addBusinessDays(option.max_days, now);
  if (from.getTime() === to.getTime()) return `chega em ${dayMonth.format(to)}`;
  return month.format(from) === month.format(to)
    ? `chega entre ${dayOnly.format(from)} e ${dayMonth.format(to)}`
    : `chega entre ${dayMonth.format(from)} e ${dayMonth.format(to)}`;
}

/** O prazo em dias úteis, para quem quer o número: "13 a 21 dias úteis". */
export const businessDays = (o: Pick<ShippingOption, "min_days" | "max_days">) =>
  o.min_days === o.max_days ? `${o.max_days} dias úteis` : `${o.min_days} a ${o.max_days} dias úteis`;

/** A data prometida do pedido ("2026-11-27") por extenso: "27 de nov.". */
export const promised = (date: string) => dayMonth.format(new Date(`${date}T12:00:00Z`));

/** O CEP que o cliente digitou por último (produto, carrinho, checkout): cotar de novo sem redigitar. */
export const ZIP_STORAGE_KEY = "cf_cep";
