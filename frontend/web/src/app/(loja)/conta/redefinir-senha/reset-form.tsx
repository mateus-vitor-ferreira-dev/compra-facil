"use client";

import Link from "next/link";
import { useActionState, useEffect, useRef, useState, type FormEvent } from "react";
import { buttonVariants } from "@/components/ui/button";
import { PasswordField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { newPasswordProblems } from "@/lib/auth/password-rules";
import { resetPassword, type ResetState } from "../../password-actions";

const linkClass = "text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4";

/**
 * A senha nova pelo link do e-mail. A troca pede um envio (não acontece ao abrir a página): leitores de
 * e-mail que abrem os links para checar segurança não gastam o link, que vale uma vez só.
 */
export function ResetForm({ accountId, token }: { accountId: string; token: string }) {
  const [state, action] = useActionState<ResetState, FormData>(resetPassword, {});
  // o aviso do navegador, antes de enviar; a server action confere de novo (sem JavaScript)
  const [local, setLocal] = useState<ResetState["fields"]>();
  const form = useRef<HTMLFormElement>(null);
  const next = useRef<HTMLAnchorElement>(null);

  // trocada (ou link inválido): o formulário (e o botão focado) sai, e o foco vai para o próximo passo (#273).
  // Senha recusada: o foco vai para o campo a corrigir
  useEffect(() => {
    if (state.status === "done" || state.status === "invalid") next.current?.focus();
    else focusInvalid(form.current, state.fields);
  }, [state]);

  if (state.status === "done") {
    return (
      <div role="status" className="space-y-4">
        <p className="text-success font-semibold">Senha trocada. Agora é só entrar com a senha nova.</p>
        <Link ref={next} href="/entrar" className={buttonVariants()}>
          Entrar
        </Link>
      </div>
    );
  }
  if (!accountId || !token || state.status === "invalid") {
    return (
      <div role="alert" className="space-y-4">
        <p className="text-danger font-medium">
          Este link venceu ou já foi usado (ele vale por 1 hora e uma vez só). Peça outro para criar a senha
          nova.
        </p>
        <Link ref={next} href="/conta/esqueci-a-senha" className={buttonVariants()}>
          Pedir outro link
        </Link>
      </div>
    );
  }

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    const data = new FormData(event.currentTarget);
    const problems = newPasswordProblems(
      String(data.get("password") ?? ""),
      String(data.get("confirm") ?? ""),
    );
    if (problems.password || problems.confirm) {
      event.preventDefault();
      setLocal(problems);
      focusInvalid(event.currentTarget, problems);
      return;
    }
    setLocal(undefined);
  };

  const fields = local ?? state.fields ?? {};
  return (
    <form ref={form} action={action} onSubmit={onSubmit} className="space-y-5" noValidate>
      <input type="hidden" name="conta" value={accountId} />
      <input type="hidden" name="token" value={token} />
      <PasswordField
        label="Senha nova"
        name="password"
        autoComplete="new-password"
        required
        minLength={8}
        maxLength={128}
        hint="Pelo menos 8 caracteres. Uma frase fácil de lembrar funciona bem."
        error={fields.password}
      />
      <PasswordField
        label="Repita a senha nova"
        name="confirm"
        autoComplete="new-password"
        required
        minLength={8}
        maxLength={128}
        error={fields.confirm}
      />
      {state.status === "error" && (
        <p role="alert" className="text-danger font-medium">
          {state.error}
        </p>
      )}
      <SubmitButton className="w-full" pendingLabel="Trocando a senha…">
        Trocar a senha
      </SubmitButton>
      <p className="text-ink-soft">
        Lembrou a senha?{" "}
        <Link href="/entrar" className={linkClass}>
          Entrar
        </Link>
      </p>
    </form>
  );
}

function focusInvalid(form: HTMLFormElement | null, fields: ResetState["fields"]) {
  const name = fields?.password ? "password" : fields?.confirm ? "confirm" : undefined;
  if (name) form?.querySelector<HTMLInputElement>(`input[name=${name}]`)?.focus();
}
