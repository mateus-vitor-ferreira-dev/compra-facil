"use client";

import { useActionState } from "react";
import { scheduleMaintenance } from "@/app/admin/manutencao/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

/** Agendar a janela de manutenção (#370), no horário de Brasília. */
export function MaintenanceForm({ initial }: { initial: { starts: string; ends: string; message: string } }) {
  const [state, action] = useActionState<FormState, FormData>(scheduleMaintenance, {});
  const v = state.values ?? initial;
  const err = state.fields ?? {};
  return (
    <form action={action} className="max-w-xl space-y-4" noValidate>
      <FormError error={state.error} />
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          label="Começa em"
          name="starts"
          type="datetime-local"
          defaultValue={v.starts}
          error={err.starts}
        />
        <TextField
          label="Termina em"
          name="ends"
          type="datetime-local"
          defaultValue={v.ends}
          error={err.ends}
        />
      </div>
      <TextField
        label="Aviso para quem compra (opcional)"
        name="message"
        maxLength={200}
        defaultValue={v.message}
        error={err.message}
        hint="Aparece depois do horário na faixa. Ex.: Os pedidos feitos antes continuam normalmente."
      />
      <p className="text-ink-soft text-sm">
        Horário de Brasília. A faixa aparece na loja assim que a janela é salva.
      </p>
      <SubmitButton pendingLabel="Agendando…">Agendar a manutenção</SubmitButton>
    </form>
  );
}
