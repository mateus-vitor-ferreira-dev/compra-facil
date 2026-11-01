"use client";

import { useEffect, useRef, useState } from "react";
import { cancelOrder, retryOrder } from "@/app/admin/atrasos/actions";
import { Button } from "@/components/ui/button";
import { SubmitButton } from "@/components/ui/submit-button";

/**
 * O que a equipe faz com um pedido parado (#225). Cancelar estorna o cliente e não tem volta, então pede uma
 * confirmação que diz isso antes de valer; o foco vai à pergunta e volta ao botão se a equipe desiste.
 */
export function SupplierOrderActions({
  orderId,
  number,
  canRetry,
  canCancel,
}: {
  orderId: string;
  number: string;
  canRetry: boolean;
  canCancel: boolean;
}) {
  const [confirming, setConfirming] = useState(false);
  const trigger = useRef<HTMLButtonElement>(null);
  const question = useRef<HTMLParagraphElement>(null);
  const opened = useRef(false);

  useEffect(() => {
    if (confirming) question.current?.focus();
    else if (opened.current) trigger.current?.focus();
    opened.current = confirming;
  }, [confirming]);

  if (!canRetry && !canCancel) return null;
  if (confirming) {
    return (
      <div className="border-ink bg-surface mt-2 space-y-3 rounded-md border-2 p-3">
        <p ref={question} tabIndex={-1} className="max-w-prose font-medium outline-none">
          Cancelar o pedido {number}? O fornecedor é avisado, o cliente é estornado por inteiro e o pedido não
          volta.
        </p>
        <form action={cancelOrder} className="flex flex-wrap gap-2">
          <input type="hidden" name="order_id" value={orderId} />
          <SubmitButton variant="danger" pendingLabel="Cancelando…">
            Cancelar e estornar
          </SubmitButton>
          <Button variant="ghost" onClick={() => setConfirming(false)}>
            Manter o pedido
          </Button>
        </form>
      </div>
    );
  }
  return (
    <div className="flex flex-wrap gap-2 pt-1">
      {canRetry && (
        <form action={retryOrder}>
          <input type="hidden" name="order_id" value={orderId} />
          <SubmitButton
            variant="secondary"
            pendingLabel="Pedindo…"
            aria-label={`Tentar de novo agora o pedido ${number}`}
          >
            Tentar de novo agora
          </SubmitButton>
        </form>
      )}
      {canCancel && (
        <Button
          ref={trigger}
          variant="ghost"
          aria-label={`Cancelar o pedido ${number}`}
          onClick={() => setConfirming(true)}
        >
          Cancelar o pedido
        </Button>
      )}
    </div>
  );
}
