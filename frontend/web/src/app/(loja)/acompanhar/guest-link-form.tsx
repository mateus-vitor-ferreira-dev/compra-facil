"use client";

import { useActionState, useEffect, useRef } from "react";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { requestGuestLink, type ResendState } from "./actions";

/** Pede o link de novo; funciona sem JavaScript. */
export function GuestLinkForm() {
  const [state, action] = useActionState<ResendState, FormData>(requestGuestLink, {});
  const form = useRef<HTMLFormElement>(null);
  // o erro do servidor (e-mail inválido, muitas tentativas) fica no campo: o foco vai até ele, e o leitor de
  // tela lê o erro junto com o rótulo (#273)
  useEffect(() => {
    if (state.error) form.current?.querySelector<HTMLInputElement>("[aria-invalid=true]")?.focus();
  }, [state]);
  return (
    <form ref={form} action={action} noValidate className="space-y-4">
      <TextField
        id="acompanhar-email"
        label="E-mail usado na compra"
        name="email"
        type="email"
        spellCheck={false}
        autoCapitalize="none"
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
