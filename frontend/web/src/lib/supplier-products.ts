/**
 * O código do produto no fornecedor (Montink) de cada cor à venda, como o painel escreve (#483). Um código só vale
 * para todas as cores, que na Montink costumam ser o mesmo produto (ex.: `6167609`); quando uma cor é outro produto
 * lá (o Vermelho), vai por cor: `Branco: 6167609, Preto: 6167609, Vermelho: 6170123`. No produto sem cor à escolha,
 * a chave é vazia, como no catalog.
 */
export type SupplierProducts = Record<string, string>;

const CODE = /^[A-Za-z0-9][A-Za-z0-9_-]{0,63}$/;

export type ParsedSupplierProducts = { ok: true; value: SupplierProducts } | { ok: false; error: string };

const same = (a: string, b: string) => a.toLocaleLowerCase("pt-BR") === b.toLocaleLowerCase("pt-BR");

/** Lê o campo do formulário com as cores à venda; vazio é `{}` (sem fornecedor). */
export function parseSupplierProducts(text: string, colors: string[]): ParsedSupplierProducts {
  const raw = text.trim();
  if (!raw) return { ok: true, value: {} };
  if (!raw.includes(":")) {
    if (!CODE.test(raw)) {
      return { ok: false, error: "O código tem só letras, dígitos e hífen (ex.: 6167609)." };
    }
    if (colors.length === 0) return { ok: true, value: { "": raw } };
    return { ok: true, value: Object.fromEntries(colors.map((c) => [c, raw])) };
  }
  if (colors.length === 0) {
    return { ok: false, error: "O produto não tem cor à escolha: informe um código só, sem a cor." };
  }
  const value: SupplierProducts = {};
  for (const entry of raw.split(/[,;\n]/)) {
    if (!entry.trim()) continue;
    const [name, code, ...rest] = entry.split(":").map((s) => s.trim());
    const color = colors.find((c) => same(c, name ?? ""));
    if (!color) {
      return { ok: false, error: `A cor “${name}” não está nas cores do produto.` };
    }
    if (rest.length || !code || !CODE.test(code)) {
      return { ok: false, error: `Escreva a cor e o código assim: ${color}: 6167609.` };
    }
    if (value[color]) return { ok: false, error: `A cor ${color} aparece mais de uma vez.` };
    value[color] = code;
  }
  const missing = colors.filter((c) => !value[c]);
  if (missing.length) {
    return {
      ok: false,
      error: `Falta o código de ${missing.join(", ")}. Tire a cor das cores do produto ou informe o código dela.`,
    };
  }
  return { ok: true, value };
}

/** Escreve os códigos para o campo: um só quando todas as cores usam o mesmo produto. */
export function formatSupplierProducts(value: SupplierProducts | undefined, colors: string[]): string {
  const entries = Object.entries(value ?? {});
  if (entries.length === 0) return "";
  const codes = new Set(entries.map(([, code]) => code));
  const coversColors = colors.length === 0 || colors.every((c) => value?.[c]);
  if (codes.size === 1 && coversColors && entries.length === Math.max(colors.length, 1)) {
    return entries[0]![1];
  }
  const ordered = [
    ...colors.filter((c) => value?.[c]),
    ...entries.map(([c]) => c).filter((c) => !colors.includes(c)),
  ];
  return ordered.map((c) => `${c || "sem cor"}: ${value![c]}`).join(", ");
}

/** O produto à venda tem fornecedor? (O do feed de dropshipping tem o dele.) */
export function hasSupplierProducts(value: SupplierProducts | undefined): boolean {
  return Object.keys(value ?? {}).length > 0;
}
