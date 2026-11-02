"use client";

import { useActionState, useEffect, useRef } from "react";
import { PasswordField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { linkGoogle, type LinkState } from "./actions";

export function LinkForm({ email }: { email: string }) {
  const [state, action] = useActionState<LinkState, FormData>(linkGoogle, {});
  const form = useRef<HTMLFormElement>(null);
  // "senha incorreta" fica no campo: o foco volta para ele, e o leitor de tela lê o erro (#273)
  useEffect(() => {
    if (state.error) form.current?.querySelector<HTMLInputElement>("[aria-invalid=true]")?.focus();
  }, [state]);
  // a senha é a única coisa a fazer aqui: com mouse, já começa nela. No celular, não: o teclado aberto
  // esconderia a explicação acima do campo (#273)
  useEffect(() => {
    if (matchMedia("(pointer: fine)").matches)
      form.current?.querySelector<HTMLInputElement>("input[name=password]")?.focus();
  }, []);
  return (
    <form ref={form} action={action} className="space-y-4">
      {/* o e-mail, para o gerenciador de senhas achar a senha certa */}
      <input type="email" name="username" value={email} autoComplete="username" readOnly hidden />
      <PasswordField
        label="Senha da conta"
        name="password"
        autoComplete="current-password"
        error={state.error}
        required
      />
      <SubmitButton className="w-full" pendingLabel="Vinculando…">
        Vincular e entrar
      </SubmitButton>
    </form>
  );
}
