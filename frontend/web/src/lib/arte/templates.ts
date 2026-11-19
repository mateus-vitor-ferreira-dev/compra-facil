/**
 * As artes que recebem o texto do cliente (a personalização do produto). O id da arte, guardado no produto do
 * catalog (personalization.template), é `base` ou `base:variante`:
 *
 * - a base diz o desenho: `nome-classico` (kicker pequeno + o texto grande), `familia-sobrenome` ("FAMÍLIA" + o
 *   sobrenome), `desde-ano` (kicker + o ano ou a frase curta) e `caneca-nome` (o nome nos dois lados da caneca);
 * - a variante escolhe os textos fixos (o kicker em cima e o rodapé) e o gorro de Natal. Sem variante, a base usa
 *   os dela.
 *
 * O tamanho do arquivo vem da peça (PRINT_SIZES): a mesma arte serve a camiseta adulto, a infantil, o body e a
 * camisa pet; a caneca tem a base própria.
 */

export type PieceKind = "adulto" | "infantil" | "body" | "pet" | "caneca";

/** Os tamanhos de impressão da Montink em 300 DPI (a área máxima de cada peça). */
export const PRINT_SIZES: Record<PieceKind, { width: number; height: number; label: string }> = {
  adulto: { width: 3508, height: 4961, label: "Camiseta adulto (A3)" },
  infantil: { width: 2598, height: 2835, label: "Camiseta infantil" },
  body: { width: 1535, height: 1890, label: "Body de bebê" },
  pet: { width: 2598, height: 2126, label: "Camisa pet" },
  caneca: { width: 2480, height: 1122, label: "Caneca (os dois lados)" },
};

export const PALETTE = { red: "#BE1628", green: "#185C3A" } as const;

type Variant = {
  /** o nome na lista do painel */
  label: string;
  /** o texto pequeno em cima, em caixa alta */
  kicker?: string;
  /** o texto pequeno embaixo */
  footer?: string;
  /** o gorro de Natal na primeira letra do texto grande */
  hat?: boolean;
  /** a cor do texto grande (as estampas claras: sem branco, que a caneca não imprime) */
  color?: string;
};

type Base = {
  label: string;
  /** a variante sem o ":" */
  default: Variant;
  variants: Record<string, Variant>;
  /** a dica do campo do cliente no painel (o que ele escreve) */
  hint: string;
};

// os papéis da família, comuns a nome-classico e caneca-nome: o cliente escreve o nome de quem dá sentido ao papel
const roles: Record<string, Variant> = {
  mae: { label: "Mãe de…", kicker: "MÃE DE" },
  pai: { label: "Pai de…", kicker: "PAI DE" },
  vova: { label: "Vovó de…", kicker: "VOVÓ DE" },
  vovo: { label: "Vovô de…", kicker: "VOVÔ DE" },
  dinda: { label: "Dinda de…", kicker: "DINDA DE" },
  dindo: { label: "Dindo de…", kicker: "DINDO DE" },
  tia: { label: "Tia de…", kicker: "TIA DE" },
  tio: { label: "Tio de…", kicker: "TIO DE" },
  amor: { label: "Meu amor", kicker: "MEU AMOR" },
  noel: { label: "Família Noel (Natal)", kicker: "FAMÍLIA NOEL", footer: "NATAL 2026", hat: true },
  aniversario: { label: "Aniversário", kicker: "ANIVERSÁRIO" },
};

export const TEMPLATES: Record<string, Base> = {
  "nome-classico": {
    label: "Nome clássico",
    default: { label: "Só o nome" },
    variants: roles,
    hint: "o nome",
  },
  "familia-sobrenome": {
    label: "Família + sobrenome",
    default: { label: "Família", kicker: "FAMÍLIA" },
    variants: { natal: { label: "Família (Natal)", kicker: "FAMÍLIA", footer: "NATAL 2026", hat: true } },
    hint: "o sobrenome",
  },
  "desde-ano": {
    label: "Desde (ano)",
    default: { label: "Desde", kicker: "DESDE" },
    variants: {
      casal: { label: "Juntos desde", kicker: "JUNTOS DESDE" },
      familia: { label: "Família desde", kicker: "FAMÍLIA DESDE" },
      amigos: { label: "Amigos desde", kicker: "AMIGOS DESDE" },
    },
    hint: "o ano ou a frase curta",
  },
  "caneca-nome": {
    label: "Caneca com nome",
    default: { label: "Só o nome" },
    variants: roles,
    hint: "o nome",
  },
};

/** Todas as artes para o select do painel: [id, rótulo]. */
export const TEMPLATE_OPTIONS: [string, string][] = Object.entries(TEMPLATES).flatMap(([id, base]) => [
  [id, `${base.label}: ${base.default.label}`] as [string, string],
  ...Object.entries(base.variants).map(
    ([v, variant]) => [`${id}:${v}`, `${base.label}: ${variant.label}`] as [string, string],
  ),
]);

export type ResolvedTemplate = {
  id: string;
  base: string;
  kicker?: string;
  footer?: string;
  hat: boolean;
  color: string;
  /** a caneca repete a arte nos dois lados */
  twoSides: boolean;
};

/** A arte do id, ou undefined se o id não existe (o produto aponta para uma arte removida). */
export function resolveTemplate(id: string): ResolvedTemplate | undefined {
  const [baseId = "", variantId] = id.split(":");
  const base = TEMPLATES[baseId];
  if (!base) return undefined;
  const variant = variantId === undefined ? base.default : base.variants[variantId];
  if (!variant) return undefined;
  return {
    id,
    base: baseId,
    kicker: variant.kicker,
    footer: variant.footer,
    hat: variant.hat ?? false,
    color: variant.color ?? PALETTE.red,
    twoSides: baseId === "caneca-nome",
  };
}

/**
 * A peça que a arte provavelmente é, pelo nome do item do pedido: a caneca pela arte; o resto pelo nome (o painel
 * deixa trocar antes de baixar).
 */
export function guessPiece(templateId: string, itemName: string): PieceKind {
  if (templateId.startsWith("caneca-") || /caneca/iu.test(itemName)) return "caneca";
  if (/\bbody\b/iu.test(itemName)) return "body";
  if (/\bpet\b|cachorr|gato/iu.test(itemName)) return "pet";
  if (/infantil|criança|kids|noelzinh/iu.test(itemName)) return "infantil";
  return "adulto";
}

/** O nome do arquivo de impressão: o pedido, a peça e o texto, sem acento nem espaço. */
export function artFileName(orderId: string, piece: PieceKind, text: string): string {
  const slug = text
    .normalize("NFD")
    .replace(/\p{Diacritic}/gu, "")
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 30);
  return `arte-${orderId.slice(0, 8)}-${piece}-${slug || "texto"}.png`;
}
