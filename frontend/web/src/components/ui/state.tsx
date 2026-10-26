import type { ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * Tela vazia ou de erro: o que aconteceu e o que fazer agora (a ação vem em `action`). Erro não pede
 * desculpas nem fica vago; vazio é um convite para agir.
 */
export function StateMessage({
  illustration,
  title,
  description,
  action,
  tone = "empty",
  headingLevel = 2,
  className,
}: {
  illustration?: ReactNode;
  title: string;
  description: string;
  action?: ReactNode;
  tone?: "empty" | "error";
  /** o nível certo na página (h2 numa tela, h3 dentro de uma seção) */
  headingLevel?: 2 | 3;
  className?: string;
}) {
  const Heading = headingLevel === 3 ? "h3" : "h2";
  return (
    <section
      role={tone === "error" ? "alert" : undefined}
      className={cn("flex max-w-md flex-col items-start gap-4 py-10", className)}
    >
      {illustration}
      <div className="space-y-1">
        <Heading className="text-xl font-semibold tracking-tight text-balance">{title}</Heading>
        <p className="text-ink-soft">{description}</p>
      </div>
      {action}
    </section>
  );
}
