"use client";

import { useEffect, useRef, useState, useSyncExternalStore } from "react";
import { Button } from "@/components/ui/button";
import type { OrdersSchemas } from "@/lib/api/client";
import { cn } from "@/lib/utils";

type Pix = OrdersSchemas["schemas"]["Pix"];

const clock = new Intl.DateTimeFormat("pt-BR", {
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});
// o aviso de que está acabando: uma vez, a 5 minutos do fim
const WARN_MS = 5 * 60 * 1000;

function remaining(ms: number) {
  const s = Math.max(0, Math.ceil(ms / 1000));
  return `${String(Math.floor(s / 60)).padStart(2, "0")}:${String(s % 60).padStart(2, "0")}`;
}

// o relógio da página, de segundo em segundo, compartilhado; nulo no servidor (a hora vem só do navegador)
let tick = 0;
let timer: ReturnType<typeof setInterval> | undefined;
const listeners = new Set<() => void>();

function subscribe(listener: () => void) {
  listeners.add(listener);
  timer ??= setInterval(() => {
    tick = Date.now();
    for (const l of listeners) l();
  }, 1000);
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      clearInterval(timer);
      timer = undefined;
      tick = 0;
    }
  };
}

function nowSnapshot() {
  if (!tick) tick = Date.now();
  return tick;
}

const useNow = () => useSyncExternalStore<number | null>(subscribe, nowSnapshot, () => null);

/**
 * O PIX do pedido para o cliente pagar: o valor, quanto tempo falta, o copia e cola e o QR. No celular,
 * o copia e cola vem antes (o QR é para ler com outro aparelho). Quando o banco confirma, o pedido muda
 * sozinho (o stream do orders) e este painel sai.
 */
export function PixPayment({ pix, amount, startedAt }: { pix: Pix; amount: string; startedAt: string }) {
  const now = useNow();
  const [copied, setCopied] = useState<"ok" | "manual" | null>(null);
  const code = useRef<HTMLTextAreaElement>(null);

  const expiresAt = Date.parse(pix.expires_at);
  const left = now === null ? null : expiresAt - now;
  const expired = left !== null && left <= 0;
  const total = Math.max(1, expiresAt - Date.parse(startedAt));
  const share = left === null ? 1 : Math.min(1, Math.max(0, left / total));
  const ending = left !== null && left <= WARN_MS;
  // contando, o leitor de tela ouve a hora em que vence, não cada segundo
  const counting = left !== null && !expired;

  useEffect(() => {
    if (copied !== "ok") return;
    const timer = setTimeout(() => setCopied(null), 4000);
    return () => clearTimeout(timer);
  }, [copied]);

  async function copy() {
    try {
      await navigator.clipboard.writeText(pix.qr_code);
      setCopied("ok");
    } catch {
      // sem permissão para a área de transferência: o código fica selecionado para copiar à mão
      code.current?.select();
      setCopied("manual");
    }
  }

  return (
    <section aria-labelledby="pix-titulo" className="border-ink bg-surface rounded-lg border-2 p-5 sm:p-6">
      <div className="flex flex-wrap items-end justify-between gap-x-8 gap-y-3">
        <div>
          <h2 id="pix-titulo" className="font-semibold">
            Valor do PIX
          </h2>
          <p className="font-condensed text-4xl leading-none font-extrabold tracking-tight">{amount}</p>
        </div>
        <p className="sm:text-right">
          <span aria-hidden={counting || undefined} className="text-ink-soft block text-sm">
            {expired ? "Venceu às" : "Vence em"}
          </span>
          <span
            aria-hidden={counting || undefined}
            className={cn(
              "font-condensed block text-4xl leading-none font-extrabold tabular-nums",
              ending && "text-danger",
            )}
          >
            {left === null || expired ? clock.format(expiresAt) : remaining(left)}
          </span>
          {left !== null && !expired && <span className="sr-only">Vence às {clock.format(expiresAt)}</span>}
        </p>
      </div>

      {/* o prazo escoando: o mesmo trilho da linha de etapas */}
      <div aria-hidden="true" className="bg-line mt-4 h-1 overflow-hidden rounded-full">
        <div
          className={cn(
            "h-full origin-left transition-transform duration-1000 ease-linear motion-reduce:transition-none",
            ending ? "bg-danger" : "bg-ink",
          )}
          style={{ transform: `scaleX(${share})` }}
        />
      </div>

      <p aria-live="polite" className="sr-only">
        {ending && !expired ? "Faltam menos de 5 minutos para o PIX vencer." : ""}
        {copied === "ok" ? "Código copiado." : ""}
      </p>

      {expired ? (
        <div role="status" className="mt-5 space-y-1">
          <p className="font-semibold">O PIX venceu.</p>
          <p className="text-ink-soft">
            O pedido vai ser cancelado e nada foi cobrado. Para comprar, faça um pedido novo.
          </p>
        </div>
      ) : (
        <div className="mt-6 grid gap-6 sm:grid-cols-[11rem_minmax(0,1fr)] sm:items-start">
          <figure className="order-last space-y-2 sm:order-first">
            {/* o QR em branco também no tema escuro: a câmera do banco precisa do contraste e da margem */}
            <div className="border-line w-44 rounded-md border bg-white p-2">
              {/* eslint-disable-next-line @next/next/no-img-element -- PNG em data URL, gerado pelo provedor */}
              <img
                src={`data:image/png;base64,${pix.qr_code_base64}`}
                alt={`QR code do PIX de ${amount}`}
                width={160}
                height={160}
                className="size-40 [image-rendering:pixelated]"
              />
            </div>
            <figcaption className="text-ink-soft text-sm">
              Ou leia o QR com a câmera do app do banco.
            </figcaption>
          </figure>

          <div className="space-y-3">
            <p>No app do banco, escolha PIX copia e cola e cole este código:</p>
            <label htmlFor="pix-codigo" className="sr-only">
              Código PIX copia e cola
            </label>
            <textarea
              id="pix-codigo"
              ref={code}
              readOnly
              rows={3}
              value={pix.qr_code}
              onFocus={(e) => e.currentTarget.select()}
              className="border-field-line bg-paper text-ink field-sizing-content w-full resize-none rounded-md border p-3 text-sm break-all outline-none focus-visible:outline-2"
            />
            <Button onClick={copy} className="w-full sm:w-auto">
              {copied === "ok" ? "Código copiado" : "Copiar o código"}
            </Button>
            {copied === "manual" && (
              <p className="text-ink-soft text-sm">O código está selecionado: copie com Ctrl+C ou o menu.</p>
            )}
            {pix.ticket_url && (
              <p className="text-sm">
                <a
                  href={pix.ticket_url}
                  target="_blank"
                  rel="noreferrer"
                  className="inline-flex min-h-11 items-center font-semibold underline underline-offset-4 sm:min-h-6"
                >
                  Abrir a página do PIX
                  <span className="sr-only"> (abre em outra aba)</span>
                </a>
              </p>
            )}
          </div>
        </div>
      )}
    </section>
  );
}
