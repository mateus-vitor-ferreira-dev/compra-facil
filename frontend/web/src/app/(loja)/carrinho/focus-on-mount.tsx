"use client";

import { useEffect, useRef } from "react";

/**
 * O título que recebe o foco quando aparece: a confirmação de esvaziar chega numa página nova (funciona sem
 * JavaScript), e com ele o foco e o leitor de tela vão direto para a pergunta, e não para o começo da página.
 */
export function FocusHeading({ id, children }: { id: string; children: React.ReactNode }) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => heading.current?.focus(), []);
  return (
    <h2 ref={heading} id={id} tabIndex={-1} className="font-semibold outline-none">
      {children}
    </h2>
  );
}
