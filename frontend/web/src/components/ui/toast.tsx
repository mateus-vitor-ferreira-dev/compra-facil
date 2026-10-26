"use client";

import { CheckCircle2, CircleAlert, X } from "lucide-react";
import { createContext, useCallback, useContext, useMemo, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

type Toast = { id: number; tone: "success" | "error"; message: string };

const ToastContext = createContext<{ show: (tone: Toast["tone"], message: string) => void } | null>(null);

const DURATION_MS = 6000;

/**
 * Avisos curtos sobre o que acabou de acontecer ("Produto adicionado ao carrinho"). Ficam numa região
 * aria-live: o leitor de tela anuncia sem tirar o foco. Os de erro usam role="alert".
 */
export function Toaster({ children }: { children: ReactNode }) {
  const [toasts, setToasts] = useState<Toast[]>([]);
  const dismiss = useCallback((id: number) => setToasts((t) => t.filter((x) => x.id !== id)), []);
  const show = useCallback(
    (tone: Toast["tone"], message: string) => {
      const id = Date.now() + Math.random();
      setToasts((t) => [...t.slice(-2), { id, tone, message }]); // no máximo 3 na tela
      setTimeout(() => dismiss(id), DURATION_MS);
    },
    [dismiss],
  );
  const value = useMemo(() => ({ show }), [show]);

  return (
    <ToastContext value={value}>
      {children}
      <div
        aria-live="polite"
        className="fixed inset-x-4 bottom-4 z-50 flex flex-col items-center gap-2 sm:inset-x-auto sm:right-6 sm:items-end"
      >
        {toasts.map((t) => (
          <div
            key={t.id}
            role={t.tone === "error" ? "alert" : "status"}
            className={cn(
              "bg-ink text-paper animate-in slide-in-from-bottom-2 fade-in flex w-full max-w-sm items-start gap-3 rounded-md px-4 py-3 text-sm shadow-lg",
            )}
          >
            {t.tone === "success" ? (
              <CheckCircle2 aria-hidden className="text-tag mt-0.5 size-4 shrink-0" />
            ) : (
              <CircleAlert aria-hidden className="mt-0.5 size-4 shrink-0 text-[#ff9aa0]" />
            )}
            <p className="flex-1">{t.message}</p>
            <button
              type="button"
              onClick={() => dismiss(t.id)}
              aria-label="Fechar aviso"
              className="-m-2 grid size-8 shrink-0 cursor-pointer place-items-center rounded-sm opacity-70 hover:opacity-100 pointer-coarse:size-11"
            >
              <X aria-hidden className="size-4" />
            </button>
          </div>
        ))}
      </div>
    </ToastContext>
  );
}

export function useToast() {
  const ctx = useContext(ToastContext);
  if (!ctx) throw new Error("useToast fora do <Toaster>");
  return ctx;
}
