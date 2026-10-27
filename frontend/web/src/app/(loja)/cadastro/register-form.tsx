"use client";

import { MailCheck } from "lucide-react";
import { useActionState, useEffect, useRef } from "react";
import { ResendConfirmation } from "@/components/account/resend-confirmation";
import { PasswordField, TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { register, type RegisterState } from "../auth-actions";

export function RegisterForm({ returnTo }: { returnTo: string }) {
  const [state, action] = useActionState<RegisterState, FormData>(register, {});
  const form = useRef<HTMLFormElement>(null);

  const summary = useRef<HTMLDivElement>(null);

  // erro de validação: o foco vai para o resumo, que leva a cada campo a corrigir
  useEffect(() => {
    summary.current?.focus();
  }, [state]);

  if (state.confirmTo) {
    return (
      <section role="status" className="space-y-4">
        <MailCheck aria-hidden="true" className="size-10" strokeWidth={1.5} />
        <h2 className="text-xl font-semibold tracking-tight">Confirme seu e-mail</h2>
        <p className="text-ink-soft">
          A conta foi criada. Mandamos um link para{" "}
          <span className="text-ink font-semibold">{state.confirmTo}</span>: abra o e-mail e toque em
          confirmar para poder entrar.
        </p>
        <ResendConfirmation email={state.confirmTo} />
      </section>
    );
  }

  const f = state.fields ?? {};
  const labels = { name: "Nome", email: "E-mail", password: "Senha" } as const;
  const invalid = (Object.keys(labels) as (keyof typeof labels)[]).filter((k) => f[k]);
  return (
    <form ref={form} action={action} className="space-y-5" noValidate>
      <input type="hidden" name="voltar" value={returnTo} />
      {invalid.length > 0 && (
        <div
          ref={summary}
          tabIndex={-1}
          role="alert"
          aria-labelledby="cadastro-erros"
          className="border-danger bg-surface rounded-md border-l-4 p-4 outline-none focus-visible:outline-2"
        >
          <h2 id="cadastro-erros" className="font-semibold">
            {invalid.length === 1
              ? "Um campo precisa de ajuste"
              : `${invalid.length} campos precisam de ajuste`}
          </h2>
          <ul className="mt-2 space-y-1">
            {invalid.map((k) => (
              <li key={k}>
                <a
                  href={`#cadastro-${k}`}
                  className="text-danger inline-flex min-h-11 items-center font-medium underline underline-offset-4 sm:min-h-8"
                >
                  {labels[k]}: {f[k]}
                </a>
              </li>
            ))}
          </ul>
        </div>
      )}
      <TextField
        id="cadastro-name"
        label="Nome"
        name="name"
        autoComplete="name"
        required
        defaultValue={state.values?.name}
        error={f.name}
      />
      <TextField
        id="cadastro-email"
        label="E-mail"
        name="email"
        type="email"
        inputMode="email"
        autoComplete="email"
        required
        defaultValue={state.values?.email}
        error={f.email}
      />
      <PasswordField
        id="cadastro-password"
        label="Senha"
        name="password"
        autoComplete="new-password"
        required
        minLength={8}
        hint="Pelo menos 8 caracteres. Uma frase fácil de lembrar funciona bem."
        error={f.password}
      />
      {state.error && (
        <p role="alert" className="text-danger font-medium">
          {state.error}
        </p>
      )}
      <SubmitButton className="w-full" pendingLabel="Criando a conta…">
        Criar conta
      </SubmitButton>
    </form>
  );
}
