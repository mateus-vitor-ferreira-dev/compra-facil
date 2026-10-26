/**
 * Regras da vitrine que não dependem de tela: qual campanha vai no destaque, quanto falta para ela
 * acabar e como ler os filtros da URL. Puras, para testar sem servidor.
 */

type CampaignLike = { discount_percent: number; ends_at: string; starts_at?: string };

/** O destaque da home: a de maior desconto; no empate, a que acaba antes (a mais urgente). */
export function featuredCampaign<T extends CampaignLike>(campaigns: T[], now = Date.now()): T | undefined {
  return campaigns
    .filter((c) => Date.parse(c.ends_at) > now && (!c.starts_at || Date.parse(c.starts_at) <= now))
    .sort(
      (a, b) => b.discount_percent - a.discount_percent || Date.parse(a.ends_at) - Date.parse(b.ends_at),
    )[0];
}

export type Remaining = { days: number; hours: number; minutes: number; seconds: number };

/** Quanto falta até `endsAt`, ou nulo se já passou. */
export function remaining(endsAt: string, now = Date.now()): Remaining | null {
  const ms = Date.parse(endsAt) - now;
  if (!(ms > 0)) return null;
  const total = Math.floor(ms / 1000);
  return {
    days: Math.floor(total / 86400),
    hours: Math.floor((total % 86400) / 3600),
    minutes: Math.floor((total % 3600) / 60),
    seconds: total % 60,
  };
}

/** "3 dias, 4 horas e 12 minutos": o mesmo tempo, para o leitor de tela (sem os segundos). */
export function describeRemaining({ days, hours, minutes }: Remaining): string {
  const parts = [
    days && `${days} ${days === 1 ? "dia" : "dias"}`,
    hours && `${hours} ${hours === 1 ? "hora" : "horas"}`,
    `${minutes} ${minutes === 1 ? "minuto" : "minutos"}`,
  ].filter(Boolean) as string[];
  const last = parts.pop() as string;
  return parts.length ? `${parts.join(", ")} e ${last}` : last;
}

const endFormat = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "long",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});

/** "30 de outubro às 10:00", no horário de Brasília. */
export function formatEnd(endsAt: string): string {
  return endFormat.format(new Date(endsAt)).replace(",", " às");
}

type Params = Record<string, string | string[] | undefined>;

const first = (v: string | string[] | undefined) => (Array.isArray(v) ? v[0] : v)?.trim() || undefined;

/** Os filtros das listagens, lidos da URL: ?q=, ?categoria=, ?promocao=1, ?cursor=. */
export function readFilters(params: Params) {
  return {
    q: first(params.q),
    category: first(params.categoria),
    onSale: first(params.promocao) === "1",
    cursor: first(params.cursor),
  };
}

/** A URL da listagem com um filtro trocado (nulo tira o filtro); trocar filtro volta à 1ª página. */
export function withFilter(path: string, params: Params, key: string, value: string | null): string {
  const next = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    const value = first(v);
    if (value && k !== key && k !== "cursor") next.set(k, value);
  }
  if (value !== null) next.set(key, value);
  const qs = next.toString();
  return qs ? `${path}?${qs}` : path;
}

/** Sem acento e em minúsculas, para casar a busca com o nome das categorias. */
export function normalize(text: string): string {
  return text
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
}
