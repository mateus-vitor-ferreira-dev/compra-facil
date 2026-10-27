"use client";

import { useActionState } from "react";
import { saveStockAdjustment, type StockState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

const REASONS = [
  { value: "restock", label: "Reposição (chegou mercadoria)" },
  { value: "inventory_count", label: "Contagem (acerto do inventário)" },
  { value: "damage", label: "Avaria ou perda" },
  { value: "other", label: "Outro" },
];

/**
 * Ajuste do estoque físico com o motivo. A Idempotency-Key nasce com a página: o clique duplo não
 * ajusta duas vezes.
 */
export function StockForm({ productId, idempotencyKey }: { productId: string; idempotencyKey: string }) {
  const [state, action] = useActionState<StockState, FormData>(saveStockAdjustment, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  return (
    <form action={action} className="space-y-4" noValidate>
      <input type="hidden" name="id" value={productId} />
      <input type="hidden" name="idempotency_key" value={idempotencyKey} />
      <FormError error={state.error} />
      <TextField
        label="Unidades"
        name="delta"
        defaultValue={v.delta}
        error={err.delta}
        hint="Positivo entra (20), negativo sai (-3)."
        inputMode="numeric"
        required
      />
      <div className="space-y-1.5">
        <label htmlFor="reason" className="block font-medium">
          Motivo
        </label>
        <select
          id="reason"
          name="reason"
          defaultValue={v.reason ?? ""}
          required
          aria-invalid={err.reason ? true : undefined}
          aria-describedby={err.reason ? "reason-erro" : undefined}
          className="bg-surface border-ink/25 aria-invalid:border-danger h-11 w-full rounded-md border px-3"
        >
          <option value="" disabled>
            Escolha o motivo
          </option>
          {REASONS.map((r) => (
            <option key={r.value} value={r.value}>
              {r.label}
            </option>
          ))}
        </select>
        {err.reason && (
          <p id="reason-erro" className="text-danger text-sm">
            {err.reason}
          </p>
        )}
      </div>
      <TextField label="Observação (opcional)" name="note" defaultValue={v.note} maxLength={500} />
      <SubmitButton variant="secondary" pendingLabel="Ajustando…" className="w-full">
        Ajustar estoque
      </SubmitButton>
    </form>
  );
}
