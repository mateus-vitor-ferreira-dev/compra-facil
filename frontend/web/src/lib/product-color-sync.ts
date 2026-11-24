/**
 * A cor escolhida e a foto mostrada na página do produto, sempre a mesma: o seletor de cor (no formulário de
 * compra) e a galeria são componentes irmãos, então conversam por um evento do navegador, sem estado em comum.
 *
 * A foto de cada cor vem da ordem do catálogo: quando há pelo menos uma foto por cor, a foto i é a da cor i
 * (o catálogo da loja publica as fotos assim). Com menos fotos que cores, ou uma cor só, não há ligação e cada
 * componente segue sozinho.
 */

const EVENT = "embrulhado:cor-do-produto";

export type ColorSource = "seletor" | "galeria";

type Detail = { productId: string; color: string; source: ColorSource };

/** As cores ligadas às fotos (a foto i é a da cor i), ou undefined quando não há ligação. */
export function colorsByPhoto(colors: string[] | undefined, photos: number): string[] | undefined {
  return colors && colors.length > 1 && photos >= colors.length ? colors : undefined;
}

/** Avisa a página que a cor do produto mudou (no seletor ou pela foto). */
export function announceColor(productId: string, color: string, source: ColorSource) {
  window.dispatchEvent(new CustomEvent<Detail>(EVENT, { detail: { productId, color, source } }));
}

/** Escuta as mudanças de cor deste produto vindas do outro lado; devolve a função que para de escutar. */
export function onColor(productId: string, from: ColorSource, handler: (color: string) => void): () => void {
  const listener = (event: Event) => {
    const { detail } = event as CustomEvent<Detail>;
    if (detail?.productId === productId && detail.source === from) handler(detail.color);
  };
  window.addEventListener(EVENT, listener);
  return () => window.removeEventListener(EVENT, listener);
}
