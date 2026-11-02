"use client";

import { useEffect, useRef } from "react";

/**
 * O erro do formulário do painel, com o código para o suporte. A cada erro novo, o foco vai para o primeiro
 * campo marcado (aria-invalid) do mesmo formulário: no celular, o campo errado costuma estar fora da tela, e o
 * aviso sozinho não diz onde (#273). Sem campo marcado, o alerta é anunciado e o foco fica onde estava.
 */
export function FormError({ error }: { error?: { message: string; traceId?: string } }) {
  const box = useRef<HTMLDivElement>(null);
  useEffect(() => {
    if (!error) return;
    box.current?.closest("form")?.querySelector<HTMLElement>("[aria-invalid=true]")?.focus();
  }, [error]);
  if (!error) return null;
  return (
    <div ref={box} role="alert" className="border-danger bg-surface space-y-1 rounded-md border-l-4 p-4">
      <p className="font-semibold">{error.message}</p>
      {error.traceId && (
        <p className="text-ink-soft text-sm">
          Código para o suporte:{" "}
          <code className="text-ink bg-ink/8 rounded-sm px-1 text-xs break-all select-all">
            {error.traceId}
          </code>
        </p>
      )}
    </div>
  );
}
