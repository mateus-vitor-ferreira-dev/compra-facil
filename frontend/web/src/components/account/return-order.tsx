"use client";

import { useActionState, useEffect, useRef, useState } from "react";
import { desistir, type ReturnState } from "@/app/(loja)/conta/pedidos/[id]/actions";
import { Button } from "@/components/ui/button";
import { SubmitButton } from "@/components/ui/submit-button";
import { formatMoney } from "@/lib/format";
import type { Order } from "@/lib/orders-api";

const until = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "long",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});

/**
 * O direito de arrependimento (CDC, art. 49): até 7 dias depois da entrega, o cliente desiste da compra e
 * recebe de volta tudo o que pagou, com o frete. A desistência pede uma confirmação, que mostra o valor e a
 * forma do estorno antes de valer.
 */
export function ReturnOrder({
  order,
}: {
  order: Pick<Order, "id" | "total" | "returnable_until" | "payment">;
}) {
  const [confirming, setConfirming] = useState(false);
  const [state, action] = useActionState<ReturnState, FormData>(desistir, { status: "idle" });
  const trigger = useRef<HTMLButtonElement>(null);
  const panel = useRef<HTMLHeadingElement>(null);
  const opened = useRef(false);

  // o foco acompanha o que mudou: abre na pergunta, volta ao botão quando o cliente mantém o pedido
  useEffect(() => {
    if (confirming) panel.current?.focus();
    else if (opened.current) trigger.current?.focus();
    opened.current = confirming;
  }, [confirming]);

  if (!order.returnable_until) return null;
  const refundTo =
    order.payment?.method === "card" ? "no cartão usado na compra" : "na conta que pagou o PIX";

  return (
    <section aria-labelledby="desistir" className="mt-10 space-y-3">
      <h2 id="desistir" className="text-xl font-semibold tracking-tight">
        Desistir da compra
      </h2>
      <p className="text-ink-soft max-w-prose">
        Você pode desistir até{" "}
        <time dateTime={order.returnable_until} className="text-ink font-medium">
          {until.format(new Date(order.returnable_until))}
        </time>
        , 7 dias depois da entrega, sem precisar explicar o motivo.
      </p>

      {!confirming ? (
        <Button ref={trigger} variant="secondary" onClick={() => setConfirming(true)}>
          Desistir da compra
        </Button>
      ) : (
        <div className="border-ink bg-surface space-y-4 rounded-md border-2 p-5">
          <h3
            ref={panel}
            tabIndex={-1}
            className="font-condensed text-3xl leading-none font-extrabold tracking-tight outline-none"
          >
            {formatMoney(order.total)} de volta
          </h3>
          <p className="max-w-prose">
            O estorno é do valor todo, com o frete, {refundTo}. Depois, mandamos por e-mail como enviar os
            produtos de volta.
          </p>
          <form action={action} className="flex flex-wrap gap-3">
            <input type="hidden" name="order_id" value={order.id} />
            <SubmitButton pendingLabel="Registrando a desistência…">Confirmar a desistência</SubmitButton>
            <Button variant="ghost" onClick={() => setConfirming(false)}>
              Manter o pedido
            </Button>
          </form>
        </div>
      )}
      <div aria-live="polite">
        {state.status === "error" && <p className="text-danger font-medium">{state.message}</p>}
      </div>
    </section>
  );
}
