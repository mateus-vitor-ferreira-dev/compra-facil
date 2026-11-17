"use client";

import { Download } from "lucide-react";
import { CopyButton } from "@/components/ui/copy-button";

/**
 * Os códigos de recuperação, mostrados uma única vez: copiar ou baixar num .txt para guardar fora do celular
 * (gerenciador de senhas, papel). Cada código vale um login sem o app autenticador.
 */
export function RecoveryCodes({ codes }: { codes: string[] }) {
  const text = [
    "Embrulhado — códigos de recuperação da verificação em duas etapas",
    "Cada código vale um login, uma vez só.",
    "",
    ...codes,
    "",
  ].join("\n");
  return (
    <section aria-labelledby="codigos-titulo" className="space-y-3">
      <h2 id="codigos-titulo" className="text-lg font-semibold">
        Seus códigos de recuperação
      </h2>
      <p className="border-ink bg-surface rounded-md border-l-4 px-4 py-3 text-sm">
        Guarde estes códigos agora, fora do celular: eles não aparecem de novo. Sem o app autenticador, cada
        código abre o painel uma vez.
      </p>
      <ol className="bg-surface grid grid-cols-2 gap-x-6 gap-y-1 rounded-md border px-4 py-3 font-mono text-base">
        {codes.map((code) => (
          <li key={code}>{code}</li>
        ))}
      </ol>
      <div className="flex flex-wrap items-center gap-2">
        <CopyButton value={codes.join("\n")} label="os códigos" />
        <a
          href={`data:text/plain;charset=utf-8,${encodeURIComponent(text)}`}
          download="embrulhado-codigos-de-recuperacao.txt"
          className="hover:bg-surface inline-flex min-h-11 items-center gap-2 rounded-md border px-3 font-medium"
        >
          <Download aria-hidden className="size-4" />
          Baixar em .txt
        </a>
      </div>
    </section>
  );
}
