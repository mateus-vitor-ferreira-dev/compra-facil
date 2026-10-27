"use client";

import { useActionState } from "react";
import { SubmitButton } from "@/components/ui/submit-button";
import { confirmOrder, type CheckoutState } from "./actions";

/**
 * O botão que faz o pedido. A Idempotency-Key nasce com a página: o clique duplo, o reenvio e a nova
 * tentativa depois de um erro mandam a mesma chave, e o orders devolve o mesmo pedido em vez de criar
 * outro. Sem JavaScript, o formulário funciona do mesmo jeito.
 */
export function CheckoutForm({ idempotencyKey, total }: { idempotencyKey: string; total: string }) {
  const [state, action] = useActionState<CheckoutState, FormData>(confirmOrder, {});
  return (
    <form action={action} className="space-y-3">
      <input type="hidden" name="idempotency_key" value={idempotencyKey} />
      {state.error && (
        <div role="alert" className="border-danger bg-surface space-y-1 rounded-md border-l-4 p-4">
          <p className="font-semibold">{state.error.title}</p>
          <p className="text-ink-soft text-sm">{state.error.message}</p>
          {state.error.traceId && (
            <p className="text-ink-soft pt-1 text-sm">
              Se precisar do suporte, informe o código{" "}
              <code className="text-ink bg-ink/8 rounded-sm px-1 text-xs break-all select-all">
                {state.error.traceId}
              </code>
            </p>
          )}
        </div>
      )}
      <SubmitButton className="w-full" pendingLabel="Fazendo o pedido…">
        Fazer o pedido de {total}
      </SubmitButton>
    </form>
  );
}
