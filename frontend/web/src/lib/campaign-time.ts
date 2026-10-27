/**
 * O período das campanhas no painel, no horário de Brasília (a loja é brasileira; o Brasil não tem
 * horário de verão desde 2019, então o fuso é fixo em -03:00).
 */
const OFFSET = "-03:00";

const parts = new Intl.DateTimeFormat("en-CA", {
  timeZone: "America/Sao_Paulo",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
  hour: "2-digit",
  minute: "2-digit",
  hourCycle: "h23",
});

/** ISO da API → valor do campo datetime-local ("2026-11-27T00:00"), em Brasília. */
export function toLocalInput(iso: string): string {
  const p = Object.fromEntries(parts.formatToParts(new Date(iso)).map((x) => [x.type, x.value]));
  return `${p.year}-${p.month}-${p.day}T${p.hour}:${p.minute}`;
}

/** Valor do campo datetime-local, em Brasília → ISO para a API; nulo se não for uma data. */
export function fromLocalInput(value: string): string | null {
  if (!/^\d{4}-\d{2}-\d{2}T\d{2}:\d{2}$/.test(value)) return null;
  const date = new Date(`${value}:00${OFFSET}`);
  return Number.isNaN(date.getTime()) ? null : date.toISOString();
}

export type CampaignPhase = "scheduled" | "running" | "ended";

export function campaignPhase(c: { starts_at: string; ends_at: string }, now = Date.now()): CampaignPhase {
  if (Date.parse(c.starts_at) > now) return "scheduled";
  return Date.parse(c.ends_at) > now ? "running" : "ended";
}

export const PHASE_LABEL: Record<CampaignPhase, string> = {
  scheduled: "Agendada",
  running: "No ar",
  ended: "Encerrada",
};

const period = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});

/** "27 de nov., 00:00 a 30 de nov., 23:59". */
export const formatPeriod = (c: { starts_at: string; ends_at: string }) =>
  `${period.format(new Date(c.starts_at))} a ${period.format(new Date(c.ends_at))}`;
