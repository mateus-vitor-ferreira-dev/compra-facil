/**
 * O período da tela de margens (#282), em dias de Brasília (fuso fixo em -03:00, sem horário de verão desde 2019).
 * A URL leva os dias escolhidos (?de=AAAA-MM-DD&ate=AAAA-MM-DD, os dois incluídos), e a API recebe o intervalo
 * [início do "de", início do dia seguinte ao "até").
 */
const OFFSET = "-03:00";
const DAY = 24 * 60 * 60 * 1000;

const ymd = new Intl.DateTimeFormat("en-CA", {
  timeZone: "America/Sao_Paulo",
  year: "numeric",
  month: "2-digit",
  day: "2-digit",
});

/** O dia de Brasília de um instante, como AAAA-MM-DD. */
export const dayOf = (at: number) => ymd.format(new Date(at));

function startOf(day: string): number | null {
  if (!/^\d{4}-\d{2}-\d{2}$/.test(day)) return null;
  const at = Date.parse(`${day}T00:00:00${OFFSET}`);
  // 2026-02-31 vira março no Date: só vale o dia que volta igual
  return Number.isNaN(at) || dayOf(at) !== day ? null : at;
}

export type MarginPeriod = {
  /** os dias como vieram, para os campos */
  de: string;
  ate: string;
  /** o intervalo para a API */
  from?: string;
  to?: string;
  error?: string;
};

export function marginPeriod(query: { de?: string; ate?: string }): MarginPeriod {
  const de = query.de ?? "";
  const ate = query.ate ?? "";
  const start = de ? startOf(de) : undefined;
  const end = ate ? startOf(ate) : undefined;
  if (start === null || end === null) return { de, ate, error: "Use datas válidas, como 01/11/2026." };
  if (start !== undefined && end !== undefined && start > end)
    return { de, ate, error: "O início do período vem antes do fim." };
  return {
    de,
    ate,
    from: start === undefined ? undefined : new Date(start).toISOString(),
    // o "até" inclui o dia todo
    to: end === undefined ? undefined : new Date(end + DAY).toISOString(),
  };
}

/** Os atalhos: os últimos 7 e 30 dias contando hoje, e o mês corrente. */
export function presets(now = Date.now()) {
  const today = dayOf(now);
  return [
    { label: "Últimos 7 dias", de: dayOf(now - 6 * DAY), ate: today },
    { label: "Últimos 30 dias", de: dayOf(now - 29 * DAY), ate: today },
    { label: "Este mês", de: `${today.slice(0, 8)}01`, ate: today },
  ];
}
