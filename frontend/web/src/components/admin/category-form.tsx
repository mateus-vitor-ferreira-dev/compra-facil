"use client";

import { useActionState } from "react";
import { saveCategory } from "@/app/admin/categorias/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { FormError } from "./form-error";

/** Nova categoria: na raiz ou dentro de um departamento. O endereço sai do nome se ficar vazio. */
export function CategoryForm({ roots }: { roots: { id: string; name: string }[] }) {
  const [state, action] = useActionState<FormState, FormData>(saveCategory, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  return (
    <form action={action} className="space-y-4" noValidate>
      <FormError error={state.error} />
      <TextField label="Nome" name="name" defaultValue={v.name} error={err.name} required maxLength={120} />
      <TextField
        label="Endereço (opcional)"
        name="slug"
        defaultValue={v.slug}
        error={err.slug}
        hint="Fica em /categoria/endereço. Vazio: sai do nome (ex.: fones-de-ouvido)."
        maxLength={80}
      />
      <div className="space-y-1.5">
        <label htmlFor="parent_id" className="block font-medium">
          Dentro de
        </label>
        <select
          id="parent_id"
          name="parent_id"
          defaultValue={v.parent_id ?? ""}
          className="bg-surface border-ink/25 h-11 w-full rounded-md border px-3"
        >
          <option value="">Nenhum (é um departamento)</option>
          {roots.map((r) => (
            <option key={r.id} value={r.id}>
              {r.name}
            </option>
          ))}
        </select>
      </div>
      <SubmitButton pendingLabel="Criando…">Criar categoria</SubmitButton>
    </form>
  );
}
