"use client";

import { useActionState, useId } from "react";
import { instructCustomer } from "@/app/admin/devolucoes/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

const OPTIONS = [
  { value: "keep", label: "Fica com o produto", hint: "Não precisa devolver. O estorno não muda." },
  { value: "send", label: "Manda para a loja", hint: "Com o código e o endereço que você informar." },
] as const;

/**
 * O que a loja combinou com o cliente quando o fornecedor recusou a devolução já na abertura (#281). O código e
 * o endereço só aparecem no "Manda para a loja", pelo CSS (:has), e assim funcionam sem JavaScript.
 */
export function CustomerInstructionForm({ orderId, number }: { orderId: string; number: string }) {
  const [state, action] = useActionState<FormState, FormData>(instructCustomer, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  const legend = useId();

  return (
    <form
      action={action}
      className="group border-line mt-2 max-w-xl space-y-3 rounded-md border p-3"
      noValidate
    >
      <input type="hidden" name="order_id" value={orderId} />
      <FormError error={state.error} />
      <fieldset
        aria-describedby={err.instruction ? `${legend}-erro` : undefined}
        aria-invalid={err.instruction ? true : undefined}
        className="space-y-2"
      >
        <legend id={legend} className="font-semibold">
          O que o cliente do pedido {number} faz com o produto?
        </legend>
        {OPTIONS.map((o) => (
          <label
            key={o.value}
            className="has-checked:border-ink border-line flex min-h-11 cursor-pointer items-start gap-3 rounded-md border p-3"
          >
            <input
              type="radio"
              name="instruction"
              value={o.value}
              defaultChecked={v.instruction === o.value}
              className="mt-1 size-4 shrink-0"
            />
            <span>
              <span className="block font-medium">{o.label}</span>
              <span className="text-ink-soft block text-sm">{o.hint}</span>
            </span>
          </label>
        ))}
        {err.instruction && (
          <p id={`${legend}-erro`} className="text-danger text-sm font-medium">
            {err.instruction}
          </p>
        )}
      </fieldset>
      <div className="hidden space-y-3 group-has-[input[value=send]:checked]:block">
        <TextField
          label="Código da devolução"
          name="return_code"
          maxLength={64}
          defaultValue={v.return_code}
          error={err.return_code}
          hint="O cliente escreve por fora do pacote."
        />
        <TextField
          label="Endereço para o envio"
          name="return_address"
          maxLength={500}
          defaultValue={v.return_address}
          error={err.return_address}
        />
      </div>
      <SubmitButton size="sm" pendingLabel="Mandando…">
        Mandar ao cliente
      </SubmitButton>
    </form>
  );
}
