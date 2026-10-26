"use client";

import { useActionState, useEffect, useRef } from "react";
import { ResendConfirmation } from "@/components/account/resend-confirmation";
import { PasswordField, TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { login, type LoginState } from "../auth-actions";

export function LoginForm({ returnTo }: { returnTo: string }) {
  const [state, action] = useActionState<LoginState, FormData>(login, {});
  const form = useRef<HTMLFormElement>(null);

  // login recusado: o foco volta para a senha, que é o que se costuma redigitar
  useEffect(() => {
    if (state.error) form.current?.querySelector<HTMLInputElement>("input[name=password]")?.focus();
  }, [state]);
  return (
    <>
      <form ref={form} action={action} className="space-y-5" noValidate>
        <input type="hidden" name="voltar" value={returnTo} />
        <TextField
          label="E-mail"
          name="email"
          type="email"
          inputMode="email"
          autoComplete="email"
          required
          defaultValue={state.email}
        />
        <PasswordField label="Senha" name="password" autoComplete="current-password" required />
        {state.error && (
          <p role="alert" className="text-danger font-medium">
            {state.error}
          </p>
        )}
        <SubmitButton className="w-full" pendingLabel="Entrando…">
          Entrar
        </SubmitButton>
      </form>
      {state.unconfirmed && state.email && <ResendConfirmation email={state.email} />}
    </>
  );
}
