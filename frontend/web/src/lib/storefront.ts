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
const all = (v: string | string[] | undefined) =>
  (Array.isArray(v) ? v : v ? [v] : []).map((x) => x.trim()).filter(Boolean);

/** As ordens da listagem: o nome na URL (?ordem=), o da API e o texto para o cliente. */
export const SORTS = [
  { value: "relevancia", api: "relevance", label: "Mais relevantes" },
  { value: "novidades", api: "newest", label: "Novidades" },
  { value: "menor-preco", api: "price_asc", label: "Menor preço" },
  { value: "maior-preco", api: "price_desc", label: "Maior preço" },
  { value: "maior-desconto", api: "discount", label: "Maior desconto" },
] as const;
export type SortValue = (typeof SORTS)[number]["value"];
export type ApiSort = (typeof SORTS)[number]["api"];

/** Reais inteiros da URL (?preco-min=3000) em centavos; o que não é número não vira filtro. */
const reais = (v: string | string[] | undefined) => {
  const raw = first(v)?.replace(/\./g, "").replace(",", ".");
  const n = raw ? Number(raw) : NaN;
  return Number.isFinite(n) && n >= 0 && n <= 1_000_000 ? Math.round(n * 100) : undefined;
};

/**
 * Os filtros das listagens, lidos da URL: ?q=, ?categoria=, ?promocao=1, ?marca= (uma ou mais),
 * ?preco-min= e ?preco-max= (em reais), ?ordem= e ?cursor=. A ordem padrão é relevância na busca e
 * novidades no resto.
 */
export function readFilters(params: Params) {
  const q = first(params.q);
  const requested = SORTS.find((s) => s.value === first(params.ordem));
  // relevância só existe com busca
  const sort = requested && (requested.value !== "relevancia" || q) ? requested : undefined;
  return {
    q,
    category: first(params.categoria),
    onSale: first(params.promocao) === "1",
    brands: [...new Set(all(params.marca))].slice(0, 20),
    minPriceCents: reais(params["preco-min"]),
    maxPriceCents: reais(params["preco-max"]),
    sort: (sort ?? SORTS.find((s) => s.value === (q ? "relevancia" : "novidades")))!,
    sortChosen: Boolean(sort),
    cursor: first(params.cursor),
  };
}

export type Filters = ReturnType<typeof readFilters>;

/** Quantos filtros estão ligados (a ordem não conta): o número do botão "Filtrar e ordenar". */
export function activeFilterCount(f: Filters, { onSaleFixed = false } = {}): number {
  return (
    f.brands.length +
    (f.minPriceCents !== undefined || f.maxPriceCents !== undefined ? 1 : 0) +
    (f.onSale && !onSaleFixed ? 1 : 0)
  );
}

/** Os parâmetros da URL sem o cursor, preservando os que têm vários valores (?marca=). */
function carry(params: Params, skip: (key: string, value: string) => boolean): URLSearchParams {
  const next = new URLSearchParams();
  for (const [k, v] of Object.entries(params)) {
    if (k === "cursor") continue;
    for (const value of all(v)) if (!skip(k, value)) next.append(k, value);
  }
  return next;
}

const href = (path: string, qs: URLSearchParams) => (qs.size ? `${path}?${qs}` : path);

/** A URL da listagem com um filtro trocado (nulo tira o filtro); trocar filtro volta à 1ª página. */
export function withFilter(path: string, params: Params, key: string, value: string | null): string {
  const next = carry(params, (k) => k === key);
  if (value !== null) next.set(key, value);
  return href(path, next);
}

/** A URL sem um valor de um filtro de vários (tirar só a "Dell" de ?marca=Dell&marca=Acer). */
export function withoutValue(path: string, params: Params, key: string, value: string): string {
  return href(
    path,
    carry(params, (k, v) => k === key && v === value),
  );
}

/** A URL sem nenhum dos filtros de refinamento (marca, preço, promoção), mantendo busca e ordem. */
export function withoutRefinements(path: string, params: Params, keep: string[] = []): string {
  const refinements = ["marca", "preco-min", "preco-max", "promocao"].filter((k) => !keep.includes(k));
  return href(
    path,
    carry(params, (k) => refinements.includes(k)),
  );
}

/** Sem acento e em minúsculas, para casar a busca com o nome das categorias. */
export function normalize(text: string): string {
  return text
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase();
}
