"use client";

import { useActionState } from "react";
import { correctPlacement, placeOrder, shipOrder } from "@/app/admin/pedidos-a-fazer/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { reaisInput } from "@/lib/money-input";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

/** O pedido feito no fornecedor: o número dele e o que a equipe pagou (opcional, a margem usa o pago). */
export function PlaceOrderForm({ orderId, number }: { orderId: string; number: string }) {
  const [state, action] = useActionState<FormState, FormData>(placeOrder, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  return (
    <form action={action} className="space-y-3" noValidate>
      <FormError error={state.error} />
      <input type="hidden" name="order_id" value={orderId} />
      <div className="grid gap-3 sm:grid-cols-[2fr_1fr_1fr]">
        <TextField
          label="Nº do pedido no fornecedor"
          name="supplier_order_id"
          defaultValue={v.supplier_order_id}
          error={err.supplier_order_id}
          autoComplete="off"
          required
        />
        <TextField
          label="Pago nos produtos (R$)"
          name="paid_products"
          inputMode="decimal"
          defaultValue={v.paid_products}
          error={err.paid_products}
          hint="Opcional."
        />
        <TextField
          label="Pago no frete (R$)"
          name="paid_freight"
          inputMode="decimal"
          defaultValue={v.paid_freight}
          error={err.paid_freight}
          hint="Opcional."
        />
      </div>
      <SubmitButton pendingLabel="Registrando…">
        Registrar o pedido feito<span className="sr-only"> para o pedido {number}</span>
      </SubmitButton>
    </form>
  );
}

/** O rastreio do fornecedor: o cliente recebe o código assim que é gravado. */
export function TrackingForm({ orderId, number }: { orderId: string; number: string }) {
  const [state, action] = useActionState<FormState, FormData>(shipOrder, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  return (
    <form action={action} className="space-y-3" noValidate>
      <FormError error={state.error} />
      <input type="hidden" name="order_id" value={orderId} />
      <div className="grid gap-3 sm:grid-cols-[1fr_1fr_2fr]">
        <TextField
          label="Código de rastreio"
          name="number"
          defaultValue={v.number}
          error={err.number}
          required
        />
        <TextField
          label="Transportadora"
          name="carrier"
          defaultValue={v.carrier}
          error={err.carrier}
          hint="Como Correios."
        />
        <TextField
          label="Link de rastreio"
          name="url"
          type="url"
          inputMode="url"
          defaultValue={v.url}
          error={err.url}
          hint="Opcional, https://."
        />
      </div>
      <SubmitButton variant="secondary" pendingLabel="Enviando ao cliente…">
        Enviar o rastreio ao cliente<span className="sr-only"> do pedido {number}</span>
      </SubmitButton>
    </form>
  );
}

/**
 * Corrige o pedido já feito (#480): o número no fornecedor e o que a equipe pagou, como o débito do extrato do
 * fornecedor. Recolhido, com os valores de agora preenchidos; a margem passa a usar os novos.
 */
export function CorrectPlacementForm({
  orderId,
  number,
  current,
}: {
  orderId: string;
  number: string;
  current: {
    supplier_order_id?: string | null;
    paid_product_cost_cents?: number | null;
    paid_freight_cost_cents?: number | null;
  };
}) {
  const [state, action] = useActionState<FormState, FormData>(correctPlacement, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  const cents = (c?: number | null) => (c == null ? "" : reaisInput(c));
  return (
    <details open={state.error ? true : undefined}>
      <summary className="inline-flex min-h-11 cursor-pointer items-center font-semibold pointer-fine:min-h-8">
        Corrigir custo<span className="sr-only"> do pedido {number}</span>
      </summary>
      <form action={action} className="mt-2 space-y-3" noValidate>
        <p className="text-ink-soft max-w-prose text-sm">
          Use o que o fornecedor debitou de fato (o extrato da carteira). A margem passa a usar os valores
          novos, e os anteriores ficam guardados.
        </p>
        <FormError error={state.error} />
        <input type="hidden" name="order_id" value={orderId} />
        <div className="grid gap-3 sm:grid-cols-[2fr_1fr_1fr]">
          <TextField
            label="Nº do pedido no fornecedor"
            name="supplier_order_id"
            defaultValue={v.supplier_order_id ?? current.supplier_order_id ?? ""}
            error={err.supplier_order_id}
            autoComplete="off"
            required
          />
          <TextField
            label="Pago nos produtos (R$)"
            name="paid_products"
            inputMode="decimal"
            defaultValue={v.paid_products ?? cents(current.paid_product_cost_cents)}
            error={err.paid_products}
          />
          <TextField
            label="Pago no frete (R$)"
            name="paid_freight"
            inputMode="decimal"
            defaultValue={v.paid_freight ?? cents(current.paid_freight_cost_cents)}
            error={err.paid_freight}
          />
        </div>
        <SubmitButton variant="secondary" size="sm" pendingLabel="Corrigindo…">
          Salvar a correção<span className="sr-only"> do pedido {number}</span>
        </SubmitButton>
      </form>
    </details>
  );
}
