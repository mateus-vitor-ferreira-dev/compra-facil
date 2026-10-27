"use client";

import { Check, X } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { isFinal, orderHeadline, orderStatus, orderSteps, type Step } from "@/lib/order-status";
import type { Order } from "@/lib/orders-api";
import { cn } from "@/lib/utils";

const time = new Intl.DateTimeFormat("pt-BR", {
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  timeZone: "America/Sao_Paulo",
});

// cada etapa fica pelo menos isto na tela: a SAGA inteira leva cerca de um segundo
const STEP_MS = 700;

/**
 * A compra andando: a manchete diz o que acontece agora e a linha de etapas marca cada passo com o
 * horário real. Enquanto o pedido está pendente, ouve o stream do orders pelo BFF (EventSource, que
 * reconecta sozinho com Last-Event-ID) e fecha no estado final.
 */
export function OrderLive({ initial }: { initial: Order }) {
  const [shown, setShown] = useState(initial);
  const [queue, setQueue] = useState<Order[]>([]);
  const [lost, setLost] = useState(false);
  // o stream começa pelo estado atual, que a página já mostrou: não entra na fila
  const lastSeen = useRef(initial.updated_at);

  useEffect(() => {
    if (isFinal(initial.status)) return;
    const source = new EventSource(`/api/pedidos/${initial.id}/eventos`);
    source.addEventListener("order", (event) => {
      const next = JSON.parse((event as MessageEvent<string>).data) as Order;
      if (next.updated_at !== lastSeen.current) {
        lastSeen.current = next.updated_at;
        setQueue((q) => [...q, next]);
      }
      if (isFinal(next.status)) source.close();
    });
    // fechado de vez (sessão vencida, pedido sumiu, serviço fora): a tela avisa; reconectando, espera
    source.onerror = () => {
      if (source.readyState === EventSource.CLOSED) setLost(true);
    };
    return () => source.close();
  }, [initial.id, initial.status]);

  // uma etapa por vez, para o cliente ver a reserva e a cobrança mesmo quando chegam juntas
  useEffect(() => {
    const [next, ...rest] = queue;
    if (!next) return;
    const timer = setTimeout(() => {
      // o pedido terminou com o estoque reservado, mas a etapa da cobrança não chegou a ser mostrada
      if (
        isFinal(next.status) &&
        next.stock_reserved_at &&
        !shown.stock_reserved_at &&
        !isFinal(shown.status)
      ) {
        setShown({ ...shown, stock_reserved_at: next.stock_reserved_at, updated_at: next.stock_reserved_at });
        return;
      }
      setShown(next);
      setQueue(rest);
    }, STEP_MS);
    return () => clearTimeout(timer);
  }, [queue, shown]);

  const steps = orderSteps(shown);
  const status = orderStatus(shown.status, shown.cancellation_reason, shown.stock_reserved_at ?? null);
  const headline = orderHeadline(shown);
  const final = isFinal(shown.status);

  return (
    <section aria-labelledby="andamento" className="space-y-6">
      <div role="status" aria-atomic="true" className="space-y-2">
        <p
          id="andamento"
          className={cn(
            "font-condensed text-3xl leading-none font-extrabold tracking-tight sm:text-4xl",
            shown.status === "confirmed" && "text-success",
            shown.status === "cancelled" && "text-danger",
          )}
        >
          {headline}
        </p>
        <p className="text-ink-soft">{status.detail}</p>
      </div>

      <ol aria-label="Andamento do pedido" className="max-w-sm">
        {steps.map((step, i) => (
          <StepItem key={step.key} step={step} last={i === steps.length - 1} />
        ))}
      </ol>

      {lost && !final && (
        <p role="alert" className="border-ink bg-surface rounded-md border-l-4 p-4 text-sm">
          A atualização ao vivo parou.{" "}
          <a href={`/conta/pedidos/${initial.id}`} className="font-semibold underline underline-offset-4">
            Recarregue a página
          </a>{" "}
          para ver a situação do pedido.
        </p>
      )}
      {!final && (
        <noscript>
          <p className="text-ink-soft text-sm">Recarregue a página para ver o andamento.</p>
        </noscript>
      )}
    </section>
  );
}

function StepItem({ step, last }: { step: Step; last: boolean }) {
  const done = step.state === "done";
  const quiet = step.state === "upcoming" || step.state === "skipped";
  return (
    <li
      aria-current={step.state === "current" ? "step" : undefined}
      className="relative grid grid-cols-[1.75rem_minmax(0,1fr)] gap-x-3 pb-5 last:pb-0"
    >
      {/* o trilho até a próxima etapa: cheio quando esta já passou */}
      {!last && (
        <span
          aria-hidden="true"
          className={cn("absolute top-7 bottom-0 left-[0.8125rem] w-0.5", done ? "bg-ink" : "bg-line")}
        />
      )}
      <span
        aria-hidden="true"
        className={cn(
          "relative grid size-7 place-items-center rounded-full border-2 [&_svg]:size-4",
          done && "border-ink bg-ink text-paper",
          step.state === "current" && "border-ink bg-surface",
          step.state === "failed" && "border-danger bg-danger text-on-danger",
          quiet && "border-line bg-paper",
        )}
      >
        {done && <Check strokeWidth={3} />}
        {step.state === "failed" && <X strokeWidth={3} />}
        {step.state === "current" && (
          <>
            <span className="bg-ink absolute size-2.5 animate-ping rounded-full opacity-60" />
            <span className="bg-ink size-2.5 rounded-full" />
          </>
        )}
      </span>
      <div className="min-h-7 pt-0.5">
        <p
          className={cn(
            "font-medium",
            quiet && "text-ink-soft",
            step.state === "skipped" && "line-through decoration-1",
            step.state === "current" && "font-semibold",
            step.state === "failed" && "text-danger font-semibold",
          )}
        >
          {step.label}
          <span className="sr-only">{stateLabel[step.state]}</span>
        </p>
        {step.at && (
          <time dateTime={step.at} className="text-ink-soft text-sm tabular-nums">
            {time.format(new Date(step.at))}
          </time>
        )}
      </div>
    </li>
  );
}

const stateLabel: Record<Step["state"], string> = {
  done: " (feito)",
  current: " (agora)",
  failed: " (não deu certo)",
  skipped: " (não aconteceu)",
  upcoming: " (a seguir)",
};
