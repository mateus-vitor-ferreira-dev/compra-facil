"use client";

import { MailCheck } from "lucide-react";
import Link from "next/link";
import { useActionState, useEffect, useRef } from "react";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { requestPasswordReset, type ForgotState } from "../../password-actions";

export function ForgotForm() {
  const [state, action] = useActionState<ForgotState, FormData>(requestPasswordReset, {});
  const done = useRef<HTMLHeadingElement>(null);
  const email = useRef<HTMLInputElement>(null);

  // enviado: o formulário (e o botão focado) sai, e o foco vai para o aviso, que o leitor de tela lê (#273).
  // E-mail inválido: o foco volta a ele, que é o que se corrige
  useEffect(() => {
    if (state.sent) done.current?.focus();
    else if (state.field) email.current?.focus();
  }, [state]);

  if (state.sent) {
    return (
      <section role="status" className="space-y-4">
        <MailCheck aria-hidden="true" className="size-10" strokeWidth={1.5} />
        <h2 ref={done} tabIndex={-1} className="text-xl font-semibold tracking-tight outline-none">
          Confira seu e-mail
        </h2>
        <p className="text-ink-soft">
          Se houver uma conta com <span className="text-ink font-semibold">{state.email}</span>, enviamos um
          link para criar uma senha nova. Ele vale por 1 hora. Confira também o spam.
        </p>
        <p className="text-ink-soft">
          Lembrou a senha?{" "}
          <Link
            href="/entrar"
            className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
          >
            Entrar
          </Link>
        </p>
      </section>
    );
  }
  return (
    <form action={action} className="space-y-5" noValidate>
      <TextField
        ref={email}
        label="E-mail"
        name="email"
        type="email"
        spellCheck={false}
        autoCapitalize="none"
        inputMode="email"
        autoComplete="email"
        required
        defaultValue={state.email}
        error={state.field}
      />
      {state.error && (
        <p role="alert" className="text-danger font-medium">
          {state.error}
        </p>
      )}
      <SubmitButton className="w-full" pendingLabel="Enviando…">
        Mandar o link
      </SubmitButton>
      <p className="text-ink-soft">
        Lembrou a senha?{" "}
        <Link
          href="/entrar"
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Entrar
        </Link>
      </p>
    </form>
  );
}
