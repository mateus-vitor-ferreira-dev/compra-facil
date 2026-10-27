"use client";

import { useActionState } from "react";
import { saveCampaign } from "@/app/admin/campanhas/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

export type CampaignFormValues = {
  id?: string;
  name: string;
  slug: string;
  discount: string;
  starts: string;
  ends: string;
};

/** Criar e editar campanha: nome, endereço, desconto e o período no horário de Brasília. */
export function CampaignForm({ initial }: { initial: CampaignFormValues }) {
  const [state, action] = useActionState<FormState, FormData>(saveCampaign, {});
  const v = { ...initial, ...state.values };
  const err = state.fields ?? {};
  return (
    <form action={action} className="space-y-4" noValidate>
      {initial.id && <input type="hidden" name="id" value={initial.id} />}
      <FormError error={state.error} />
      <TextField label="Nome" name="name" defaultValue={v.name} error={err.name} required maxLength={120} />
      <TextField
        label="Endereço (opcional)"
        name="slug"
        defaultValue={v.slug}
        error={err.slug}
        hint="Vazio: sai do nome (ex.: black-friday-2026)."
        maxLength={80}
      />
      <TextField
        label="Desconto (%)"
        name="discount"
        defaultValue={v.discount}
        error={err.discount}
        hint="De 1 a 90, sobre o preço de tabela."
        inputMode="numeric"
        required
      />
      <div className="grid gap-4">
        <TextField
          label="Começa em"
          name="starts"
          type="datetime-local"
          defaultValue={v.starts}
          error={err.starts}
          required
        />
        <TextField
          label="Termina em"
          name="ends"
          type="datetime-local"
          defaultValue={v.ends}
          error={err.ends}
          required
        />
      </div>
      <p className="text-ink-soft text-sm">Horário de Brasília.</p>
      <SubmitButton pendingLabel="Salvando…">
        {initial.id ? "Salvar campanha" : "Criar campanha"}
      </SubmitButton>
    </form>
  );
}
