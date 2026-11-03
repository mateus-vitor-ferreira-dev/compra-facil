"use client";

import { useEffect, useRef } from "react";
import { cn } from "@/lib/utils";

/**
 * O título que recebe o foco quando aparece. Para uma pergunta que chega numa página nova (funciona sem
 * JavaScript): com ele, o foco e o leitor de tela vão direto para a pergunta, e não para o começo da página.
 */
export function FocusHeading({
  level = 2,
  id,
  className,
  children,
}: {
  level?: 1 | 2;
  id: string;
  className?: string;
  children: React.ReactNode;
}) {
  const heading = useRef<HTMLHeadingElement>(null);
  useEffect(() => heading.current?.focus(), []);
  const Tag = level === 1 ? "h1" : "h2";
  return (
    <Tag ref={heading} id={id} tabIndex={-1} className={cn("font-semibold outline-none", className)}>
      {children}
    </Tag>
  );
}
