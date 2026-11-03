/**
 * Os textos do aviso de manutenção (#370), no horário de Brasília. Puros, para a faixa da loja e o painel dizerem
 * a mesma coisa.
 */
const ZONE = "America/Sao_Paulo";
const time = new Intl.DateTimeFormat("pt-BR", { hour: "2-digit", minute: "2-digit", timeZone: ZONE });
const dayMonth = new Intl.DateTimeFormat("pt-BR", { day: "numeric", month: "short", timeZone: ZONE });
const ymd = new Intl.DateTimeFormat("en-CA", { timeZone: ZONE });
const DAY = 24 * 60 * 60 * 1000;

/** "hoje", "amanhã" ou "5 de nov.", pelo dia de Brasília. */
export function dayLabel(at: number, now = Date.now()): string {
  const day = ymd.format(at);
  if (day === ymd.format(now)) return "hoje";
  if (day === ymd.format(now + DAY)) return "amanhã";
  return dayMonth.format(at);
}

/** "hoje, das 22:00 às 22:05" (ou com o dia do fim, quando atravessa a meia-noite). */
export function windowLabel(startsAt: string, endsAt: string, now = Date.now()): string {
  const start = Date.parse(startsAt);
  const end = Date.parse(endsAt);
  const sameDay = ymd.format(start) === ymd.format(end);
  return sameDay
    ? `${dayLabel(start, now)}, das ${time.format(start)} às ${time.format(end)}`
    : `${dayLabel(start, now)} às ${time.format(start)} até ${dayLabel(end, now)} às ${time.format(end)}`;
}

/** Até que horas: "até 22:05". */
export const untilLabel = (endsAt: string) => `até ${time.format(Date.parse(endsAt))}`;
