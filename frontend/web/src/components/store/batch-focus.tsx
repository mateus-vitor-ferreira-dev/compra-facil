"use client";

import { useEffect, useRef } from "react";

/**
 * Depois de um "Carregar mais", leva o foco ao primeiro card do lote novo, sem rolar: quem usa teclado
 * ou leitor de tela continua de onde os produtos novos começam, em vez de voltar ao topo. Na primeira
 * renderização (inclusive ao voltar da página do produto) não mexe no foco.
 */
export function BatchFocus({ loaded }: { loaded: number }) {
  const previous = useRef(loaded);
  useEffect(() => {
    if (loaded > previous.current) {
      document
        .querySelector<HTMLElement>(`[data-lote="${previous.current + 1}"] a`)
        ?.focus({ preventScroll: true });
    }
    previous.current = loaded;
  }, [loaded]);
  return null;
}
