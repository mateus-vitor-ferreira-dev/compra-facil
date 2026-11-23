"use client";

import { Download } from "lucide-react";
import { useId, useState } from "react";
import { Button } from "@/components/ui/button";
import { resolveIllustrated, variantFor, type Variant } from "@/lib/arte/illustrated";
import { PRINT_SIZES, artFileName, guessPiece, resolveTemplate, type PieceKind } from "@/lib/arte/templates";

const VARIANT_LABEL: Record<Variant, string> = {
  clara: "Clara (peça branca ou clara)",
  escura: "Escura (Preto, Vinho, Roxo, Marinho)",
  vermelha: "Vermelha (peça vermelha)",
};

const select = "border-field-line bg-surface h-9 rounded-md border px-2 text-sm";

/**
 * "Baixar arte": o arquivo de impressão do texto do cliente (PNG 300 DPI, fundo transparente), gerado no navegador
 * com a arte do produto. A arte ilustrada já sabe a peça e escolhe a versão pela cor do item (dá para trocar); a
 * tipográfica adivinha a peça pelo nome do item (dá para trocar).
 */
export function ArtDownload({
  orderId,
  itemName,
  text,
  template,
  color,
}: {
  orderId: string;
  itemName: string;
  text: string;
  /** a arte do produto (personalization.template); ausente, o produto não a tem mais */
  template?: string;
  /** a cor da peça: nas escuras, a arte ilustrada sai na versão escura */
  color?: string;
}) {
  const id = useId();
  const illustrated = template ? resolveIllustrated(template) : undefined;
  const simple = template && !illustrated ? resolveTemplate(template) : undefined;
  const [piece, setPiece] = useState<PieceKind>(() => guessPiece(template ?? "", itemName));
  const [variant, setVariant] = useState<Variant>(() => variantFor(color));
  const [state, setState] = useState<"idle" | "working" | "error">("idle");

  if (!illustrated && !simple) {
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
      // o desenho só carrega quando alguém baixa: as fontes e as ilustrações não pesam no painel
      const blob = illustrated
        ? await (
            await import("@/lib/arte/render-illustrated")
          ).renderIllustratedPng(illustrated, text, variant)
        : await (await import("@/lib/arte/render")).renderArtPng(simple!, text, piece);
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = artFileName(orderId, illustrated ? illustrated.produto : piece, `${text}-${variant}`);
      a.click();
      setTimeout(() => URL.revokeObjectURL(url), 10_000);
      setState("idle");
    } catch {
      setState("error");
    }
  }

  return (
    <div className="flex flex-wrap items-end gap-2">
      {illustrated ? (
        <div className="space-y-1">
          <label htmlFor={id} className="text-ink-soft block text-xs">
            Versão ({PRINT_SIZES[illustrated.produto].label})
          </label>
          <select
            id={id}
            value={variant}
            onChange={(e) => setVariant(e.target.value as Variant)}
            className={select}
          >
            {(Object.keys(VARIANT_LABEL) as Variant[]).map((v) => (
              <option key={v} value={v}>
                {VARIANT_LABEL[v]}
              </option>
            ))}
          </select>
        </div>
      ) : (
        <div className="space-y-1">
          <label htmlFor={id} className="text-ink-soft block text-xs">
            Peça
          </label>
          <select
            id={id}
            value={piece}
            onChange={(e) => setPiece(e.target.value as PieceKind)}
            className={select}
          >
            {(Object.keys(PRINT_SIZES) as PieceKind[]).map((k) => (
              <option key={k} value={k}>
                {PRINT_SIZES[k].label}
              </option>
            ))}
          </select>
        </div>
      )}
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
