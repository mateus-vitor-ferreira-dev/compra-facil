"use client";

import { Download } from "lucide-react";
import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { renderArtPng } from "@/lib/arte/render";
import { PRINT_SIZES, artFileName, guessPiece, resolveTemplate, type PieceKind } from "@/lib/arte/templates";

/**
 * "Baixar arte": o arquivo de impressão do texto do cliente (PNG 300 DPI, fundo transparente), gerado no navegador
 * com a arte do produto. A peça vem adivinhada pelo nome do item e dá para trocar antes de baixar.
 */
export function ArtDownload({
  orderId,
  itemName,
  text,
  template,
}: {
  orderId: string;
  itemName: string;
  text: string;
  /** a arte do produto (personalization.template); ausente, o produto não a tem mais */
  template?: string;
}) {
  const id = useId();
  const resolved = template ? resolveTemplate(template) : undefined;
  const [piece, setPiece] = useState<PieceKind>(() => guessPiece(template ?? "", itemName));
  const [state, setState] = useState<"idle" | "working" | "error">("idle");

  if (!resolved) {
    return (
      <p className="text-danger text-sm">
        Sem arte para gerar{" "}
        {template ? `(a arte ${template} não existe)` : "(o produto não tem personalização)"}: faça o arquivo
        à mão com o texto acima.
      </p>
    );
  }

  async function download() {
    setState("working");
    try {
      const blob = await renderArtPng(resolved!, text, piece);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = artFileName(orderId, piece, text);
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 10_000);
      setState("idle");
    } catch {
      setState("error");
    }
  }

  return (
    <div className="flex flex-wrap items-end gap-2">
      <div className="space-y-1">
        <label htmlFor={id} className="text-ink-soft block text-xs">
          Peça
        </label>
        <select
          id={id}
          value={piece}
          onChange={(e) => setPiece(e.target.value as PieceKind)}
          className="border-field-line bg-surface h-9 rounded-md border px-2 text-sm"
        >
          {(Object.keys(PRINT_SIZES) as PieceKind[]).map((k) => (
            <option key={k} value={k}>
              {PRINT_SIZES[k].label}
            </option>
          ))}
        </select>
      </div>
      <Button type="button" variant="secondary" size="sm" onClick={download} disabled={state === "working"}>
        <Download aria-hidden="true" />
        {state === "working" ? "Gerando…" : "Baixar arte"}
      </Button>
      {state === "error" && (
        <p role="alert" className="text-danger w-full text-sm">
          A arte não foi gerada. Tente de novo; se continuar, faça o arquivo à mão.
        </p>
      )}
    </div>
  );
}
