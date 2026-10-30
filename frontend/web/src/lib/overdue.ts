const relative = new Intl.RelativeTimeFormat("pt-BR", { numeric: "always" });

/** "há 3 dias", "há 5 horas": quanto tempo passou do prazo, na maior unidade que couber. */
export function lateBy(dueAt: string, now: number): string {
  const minutes = Math.max(1, Math.floor((now - Date.parse(dueAt)) / 60_000));
  if (minutes < 60) return relative.format(-minutes, "minute");
  const hours = Math.floor(minutes / 60);
  if (hours < 48) return relative.format(-hours, "hour");
  return relative.format(-Math.floor(hours / 24), "day");
}

/** "2 min", "6 h", "3 dias": um prazo configurado, na maior unidade que couber sem arredondar para zero. */
export function deadline(hours: number): string {
  if (hours < 1) return `${Math.round(hours * 60)} min`;
  return hours < 48 ? `${Math.round(hours)} h` : `${Math.round(hours / 24)} dias`;
}
