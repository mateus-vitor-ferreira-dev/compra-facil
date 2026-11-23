/**
 * As cores das peças: o tom da bolinha do seletor e se a peça é escura (a arte de impressão muda para a versão
 * escura, com o contorno de adesivo). O nome vem do cadastro do produto; um nome que não está aqui aparece com a
 * bolinha neutra e o nome por escrito.
 */
const HEX: Record<string, string> = {
  branco: "#FFFFFF",
  "off white": "#F5F1E8",
  preto: "#141414",
  vermelho: "#C62828",
  vinho: "#6D1A2B",
  roxo: "#5E2A84",
  marinho: "#1F2A44",
  "azul marinho": "#1F2A44",
  azul: "#2F6FD1",
  "azul claro": "#9CC7EE",
  cinza: "#9E9E9E",
  "cinza mescla": "#B5B5B5",
  verde: "#2E7D32",
  "verde bandeira": "#1B7A3A",
  amarelo: "#F9D71C",
  rosa: "#F4A7B9",
  "rosa bebê": "#F8C8D4",
  bege: "#E8DCC4",
  laranja: "#EF7D22",
};

/** As peças escuras: a arte sai na versão escura. As outras (Branco e as claras) usam a clara. */
const DARK = new Set(["preto", "vinho", "roxo", "marinho", "azul marinho"]);

const key = (name: string) => name.trim().toLowerCase();

export const colorHex = (name: string): string | undefined => HEX[key(name)];

export const isDarkColor = (name: string | null | undefined) => (name ? DARK.has(key(name)) : false);
