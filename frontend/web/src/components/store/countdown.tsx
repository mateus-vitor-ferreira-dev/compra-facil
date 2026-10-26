"use client";

import { useSyncExternalStore } from "react";
import { describeRemaining, formatEnd, remaining } from "@/lib/storefront";

// um relógio só para a página inteira, que bate a cada segundo enquanto alguém escuta
const listeners = new Set<() => void>();
let timer: ReturnType<typeof setInterval> | undefined;
let now = 0;

function subscribe(listener: () => void) {
  listeners.add(listener);
  if (!timer) {
    now = Date.now();
    timer = setInterval(() => {
      now = Date.now();
      listeners.forEach((l) => l());
    }, 1000);
  }
  return () => {
    listeners.delete(listener);
    if (listeners.size === 0) {
      clearInterval(timer);
      timer = undefined;
    }
  };
}

const getSnapshot = () => now || Date.now();
// no servidor não há "agora" estável: sai a data de término, e os dígitos entram na hidratação
const getServerSnapshot = () => 0;

const pad = (n: number) => String(n).padStart(2, "0");

/**
 * Quanto falta para a campanha acabar, em dígitos de letreiro. O leitor de tela ouve a frase inteira
 * uma vez (sem região viva: não fica repetindo a cada segundo).
 */
export function Countdown({ endsAt, className }: { endsAt: string; className?: string }) {
  const clock = useSyncExternalStore(subscribe, getSnapshot, getServerSnapshot);
  const left = clock ? remaining(endsAt, clock) : null;

  if (!clock) {
    return (
      <p className={className}>
        Termina em <time dateTime={endsAt}>{formatEnd(endsAt)}</time>
      </p>
    );
  }
  if (!left) return <p className={className}>Esta campanha terminou.</p>;

  const units = [
    left.days > 0 && { value: left.days, label: left.days === 1 ? "dia" : "dias" },
    { value: left.hours, label: "h" },
    { value: left.minutes, label: "min" },
    { value: left.seconds, label: "s" },
  ].filter(Boolean) as { value: number; label: string }[];

  return (
    <p className={className}>
      <span className="sr-only">
        Termina em {describeRemaining(left)}, em <time dateTime={endsAt}>{formatEnd(endsAt)}</time>
      </span>
      <span aria-hidden="true" className="flex flex-wrap items-baseline gap-x-3 gap-y-1">
        <span>Termina em</span>
        {units.map((u) => (
          <span key={u.label} className="inline-flex items-baseline gap-0.5">
            <span className="font-condensed text-2xl leading-none font-extrabold sm:text-3xl">
              {u.label === "dia" || u.label === "dias" ? u.value : pad(u.value)}
            </span>
            <span className="text-sm">{u.label}</span>
          </span>
        ))}
      </span>
    </p>
  );
}
