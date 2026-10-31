"use client";

import { useActionState } from "react";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { requestGuestLink, type ResendState } from "./actions";

/** Pede o link de novo; funciona sem JavaScript. */
export function GuestLinkForm() {
  const [state, action] = useActionState<ResendState, FormData>(requestGuestLink, {});
  return (
    <form action={action} noValidate className="space-y-4">
      <TextField
        id="acompanhar-email"
        label="E-mail usado na compra"
        name="email"
        type="email"
        autoComplete="email"
        defaultValue={state.email}
        error={state.error}
      />
      <SubmitButton className="w-full" pendingLabel="Enviando…">
        Receber o link
      </SubmitButton>
      {state.sent && (
        <p role="status" className="border-success bg-surface rounded-md border-l-4 px-4 py-3">
          Se houver compra sem conta com esse e-mail, o link chega em alguns minutos. Confira também o spam.
        </p>
      )}
    </form>
  );
}
