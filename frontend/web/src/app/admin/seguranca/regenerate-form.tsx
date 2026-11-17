"use client";

import { useActionState, useEffect, useRef } from "react";
import { RecoveryCodes } from "@/components/account/recovery-codes";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { regenerateRecoveryCodes, type RegenerateState } from "./actions";

/** Gera códigos de recuperação novos (os de antes deixam de valer) e os mostra uma única vez. */
export function RegenerateForm() {
  const [state, action] = useActionState<RegenerateState, FormData>(regenerateRecoveryCodes, {});
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (state.error) form.current?.querySelector<HTMLInputElement>("[aria-invalid=true]")?.focus();
  }, [state]);

  if (state.codes) return <RecoveryCodes codes={state.codes} />;
  return (
    <form ref={form} action={action} className="max-w-sm space-y-4" noValidate>
      <TextField
        label="Código do app autenticador"
        name="code"
        autoComplete="one-time-code"
        inputMode="numeric"
        pattern="[0-9 ]*"
        maxLength={7}
        hint="Os códigos de recuperação de antes deixam de valer."
        error={state.error}
        required
      />
      <SubmitButton pendingLabel="Gerando…">Gerar códigos novos</SubmitButton>
    </form>
  );
}
