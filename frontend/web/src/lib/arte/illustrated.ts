import { isDarkColor } from "@/lib/colors";
import data from "./ilustradas.json";
import type { PieceKind } from "./templates";

/**
 * As artes ilustradas: a ilustração em cima (PNG de 2000 px em public/arte, com a versão "-escura" de contorno de
 * adesivo para as peças escuras) e a tipografia embaixo. O catálogo de artes é ilustradas.json (gerado com as
 * ilustrações, a mesma fonte do motor das estampas); cada uma tem a peça, as cores em que é vendida, a paleta de
 * cada versão e as linhas de texto, uma delas editável (o texto do cliente).
 *
 * No json o id é "colecao/nome"; no produto do catalog (personalization.template), "colecao:nome": a "/" não passa
 * no formato de id de arte do catalog, e o ":" é o separador de base e variante que ele aceita.
 */

export type Face = "serif-it" | "script" | "pacifico" | "anton" | "round" | "sans";

export type ArtLine = {
  k: "kicker" | "big" | "footer" | "icon";
  /** o texto padrão (o da linha editável é trocado pelo do cliente); a linha de ícone não tem */
  t?: string;
  /** o ícone da linha "icon" */
  i?: "loading" | "heart" | "star";
  /** a chave da cor na paleta (padrão: a no texto grande e no ícone, b no kicker e no rodapé) */
  c?: string;
  face?: Face;
  size?: number;
  /** o ornamento dos dois lados do kicker */
  orn?: "star" | "heart";
  editavel?: { label: string; maxLength: number };
};

/** a e b sempre; outras chaves (ex.: p rosa e z azul no chá revelação) para as linhas com `c` */
export type Palette = { a: string; b: string } & Record<string, string>;

export type IllustratedTemplate = {
  id: string;
  produto: PieceKind;
  ilustracao: string;
  ilustracaoEscura: string;
  paleta: { clara: Palette; escura: Palette; vermelha: Palette };
  cores: string[];
  linhas: ArtLine[];
};

const TEMPLATES = data as IllustratedTemplate[];

/** O id do json ("natal/papai-noel") como o catalog guarda ("natal:papai-noel"). */
export const catalogTemplateId = (jsonId: string) => jsonId.replace("/", ":");

const byCatalogId = new Map(TEMPLATES.map((t) => [catalogTemplateId(t.id), t]));

export function resolveIllustrated(catalogId: string): IllustratedTemplate | undefined {
  return byCatalogId.get(catalogId);
}

/** As artes com uma linha editável, por coleção, para o select do painel: [coleção, [id do catalog, rótulo][]]. */
export const ILLUSTRATED_OPTIONS: [string, [string, string][]][] = Object.entries(
  TEMPLATES.filter((t) => t.linhas.some((l) => l.editavel)).reduce<Record<string, [string, string][]>>(
    (acc, t) => {
      const [collection = "", name = ""] = t.id.split("/");
      (acc[collection] ??= []).push([catalogTemplateId(t.id), `${name} (${t.produto})`]);
      return acc;
    },
    {},
  ),
);

export type Variant = "clara" | "escura" | "vermelha";

/**
 * A versão da arte pela cor da peça: Branco e as claras usam a clara; Vermelho, a vermelha; as escuras (Preto,
 * Vinho, Roxo, Marinho), a escura. Sem cor (a peça só vem em uma), a clara.
 */
export function variantFor(color: string | null | undefined): Variant {
  if (color && color.trim().toLowerCase() === "vermelho") return "vermelha";
  return isDarkColor(color) ? "escura" : "clara";
}

/** As linhas com o texto do cliente na editável. */
export const linesWithText = (t: IllustratedTemplate, text: string): ArtLine[] =>
  t.linhas.map((l) => (l.editavel && text ? { ...l, t: text } : l));

/** O arquivo da ilustração da versão, em public/arte. */
export const illustrationUrl = (t: IllustratedTemplate, v: Variant) =>
  `/arte/${v === "clara" ? t.ilustracao : t.ilustracaoEscura}`;
