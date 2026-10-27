"use client";

import { useActionState } from "react";
import { PasswordField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { linkGoogle, type LinkState } from "./actions";

export function LinkForm({ email }: { email: string }) {
  const [state, action] = useActionState<LinkState, FormData>(linkGoogle, {});
  return (
    <form action={action} className="space-y-4">
      {/* o e-mail, para o gerenciador de senhas achar a senha certa */}
      <input type="email" name="username" value={email} autoComplete="username" readOnly hidden />
      <PasswordField
        label="Senha da conta"
        name="password"
        autoComplete="current-password"
        error={state.error}
        required
        autoFocus
      />
      <SubmitButton className="w-full" pendingLabel="Vinculando…">
        Vincular e entrar
      </SubmitButton>
    </form>
  );
}
