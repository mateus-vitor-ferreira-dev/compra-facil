"use client";

import { useActionState, useEffect, useRef } from "react";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { verifyMfa, type MfaState } from "./actions";

/**
 * O código do segundo passo. O do app vai com `autocomplete="one-time-code"` e teclado numérico; o de
 * recuperação, com letras. O erro fica no campo e o foco volta para ele (#273).
 */
export function MfaForm({ recovery }: { recovery: boolean }) {
  const [state, action] = useActionState<MfaState, FormData>(verifyMfa, {});
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (state.error) form.current?.querySelector<HTMLInputElement>("[aria-invalid=true]")?.focus();
  }, [state]);
  // o código é a única coisa a fazer aqui: com mouse, já começa nele (no celular, o teclado esconderia o texto)
  useEffect(() => {
    if (matchMedia("(pointer: fine)").matches)
      form.current?.querySelector<HTMLInputElement>("input:not([type=hidden])")?.focus();
  }, []);

  return (
    <form ref={form} action={action} className="space-y-4" noValidate>
      <input type="hidden" name="modo" value={recovery ? "recuperacao" : "app"} />
      {recovery ? (
        <TextField
          key="recuperacao"
          label="Código de recuperação"
          name="recovery_code"
          autoComplete="off"
          autoCapitalize="none"
          spellCheck={false}
          maxLength={32}
          hint="10 letras e números, como k7m2p-9xq4r"
          error={state.error}
          required
        />
      ) : (
        <TextField
          key="app"
          label="Código do app autenticador"
          name="code"
          autoComplete="one-time-code"
          inputMode="numeric"
          pattern="[0-9 ]*"
          maxLength={7}
          hint="6 números; o código muda a cada 30 segundos"
          error={state.error}
          required
        />
      )}
      <SubmitButton className="w-full" pendingLabel="Conferindo…">
        Confirmar
      </SubmitButton>
    </form>
  );
}
