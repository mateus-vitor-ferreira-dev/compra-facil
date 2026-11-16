"use client";

import { Check, Copy } from "lucide-react";
import { useEffect, useState } from "react";
import { cn } from "@/lib/utils";

/**
 * Copia um valor para a área de transferência, com o rótulo do que é (para o leitor de tela e para quem cola no
 * formulário do fornecedor). Confirma por 2 s, sem tirar o botão do lugar.
 */
export function CopyButton({
  value,
  label,
  className,
}: {
  value: string;
  label: string;
  className?: string;
}) {
  const [done, setDone] = useState(false);

  useEffect(() => {
    if (!done) return;
    const timer = setTimeout(() => setDone(false), 2000);
    return () => clearTimeout(timer);
  }, [done]);

  return (
    <button
      type="button"
      onClick={async () => {
        try {
          await navigator.clipboard.writeText(value);
          setDone(true);
        } catch {
          // sem permissão: o valor continua na tela para copiar à mão
        }
      }}
      className={cn(
        "text-ink-soft hover:text-ink hover:bg-ink/8 inline-flex size-11 shrink-0 cursor-pointer items-center justify-center rounded-md pointer-fine:size-8",
        done && "text-success hover:text-success",
        className,
      )}
    >
      {done ? (
        <Check aria-hidden="true" className="size-4" />
      ) : (
        <Copy aria-hidden="true" className="size-4" />
      )}
      <span className="sr-only">{done ? `${label} copiado` : `Copiar ${label}`}</span>
    </button>
  );
}
