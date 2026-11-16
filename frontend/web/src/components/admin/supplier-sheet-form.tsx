"use client";

import { CircleCheck, Upload } from "lucide-react";
import { useActionState, useEffect, useRef } from "react";
import { uploadSheet, type SheetState } from "@/app/admin/fornecedor/sheet-actions";
import { SubmitButton } from "@/components/ui/submit-button";

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;

/**
 * Sobe a planilha do fornecedor manual (#412). O resultado aparece embaixo do botão: o que mudou na loja ou, com
 * erro, cada linha a corrigir (nada entra pela metade). O foco vai para o resultado, para quem usa leitor de tela.
 */
export function SupplierSheetForm() {
  const [state, action] = useActionState<SheetState, FormData>(uploadSheet, { status: "idle" });
  const result = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (state.status !== "idle") result.current?.focus();
  }, [state]);

  return (
    <div className="space-y-4">
      <form action={action} className="max-w-prose space-y-3">
        <div className="space-y-1.5">
          <label htmlFor="planilha" className="block font-medium">
            Arquivo CSV
          </label>
          <input
            id="planilha"
            name="planilha"
            type="file"
            accept=".csv,text/csv"
            required
            aria-describedby="planilha-dica"
            className="file:bg-surface file:border-field-line block w-full text-sm file:mr-3 file:min-h-11 file:rounded-md file:border file:px-3 file:font-medium"
          />
          <p id="planilha-dica" className="text-ink-soft text-sm">
            Exportado do Excel ou do Google Planilhas, separado por ponto e vírgula ou vírgula. A planilha
            nova substitui a anterior.
          </p>
        </div>
        <SubmitButton pendingLabel="Conferindo a planilha…">
          <Upload aria-hidden="true" className="size-4" />
          Subir a planilha
        </SubmitButton>
      </form>

      <div ref={result} tabIndex={-1} aria-live="polite" className="outline-none">
        {state.status === "ok" && (
          <p role="status" className="text-success flex items-start gap-2 font-semibold">
            <CircleCheck aria-hidden="true" className="mt-0.5 size-5 shrink-0" />
            <span>
              Planilha com {plural(state.products, "produto", "produtos")}:{" "}
              {plural(state.created, "novo", "novos")}, {plural(state.updated, "alterado", "alterados")} e{" "}
              {plural(state.removed, "saiu", "saíram")} da loja. A vitrine atualiza em segundos.
            </span>
          </p>
        )}
        {state.status === "error" && (
          <p role="alert" className="text-danger font-semibold">
            {state.message}
            {state.traceId && (
              <span className="text-ink-soft block text-sm font-normal">Código: {state.traceId}</span>
            )}
          </p>
        )}
        {state.status === "invalid" && (
          <div role="alert" className="border-danger space-y-3 rounded-md border-2 p-4">
            <p className="font-semibold">
              {plural(state.errors.length, "problema", "problemas")} em {state.fileName}. Nada mudou na loja:
              corrija e suba de novo.
            </p>
            <ol className="divide-line divide-y text-sm">
              {state.errors.slice(0, 50).map((e, i) => (
                <li key={`${e.line}-${i}`} className="flex gap-3 py-2">
                  <span className="text-ink-soft w-20 shrink-0 tabular-nums">Linha {e.line}</span>
                  <span>{e.message}</span>
                </li>
              ))}
            </ol>
            {state.errors.length > 50 && (
              <p className="text-ink-soft text-sm">
                E mais {state.errors.length - 50}. Corrija estas primeiro.
              </p>
            )}
          </div>
        )}
      </div>
    </div>
  );
}
