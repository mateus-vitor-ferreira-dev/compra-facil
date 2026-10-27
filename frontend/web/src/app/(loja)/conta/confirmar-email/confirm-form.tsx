"use client";

import Link from "next/link";
import { useActionState } from "react";
import { buttonVariants } from "@/components/ui/button";
import { SubmitButton } from "@/components/ui/submit-button";
import { confirmEmail, type ConfirmState } from "../../auth-actions";

/**
 * A confirmação pede um toque em vez de acontecer ao abrir a página: leitores de e-mail que abrem os
 * links para checar segurança não confirmam a conta por conta própria.
 */
export function ConfirmForm({ accountId, token }: { accountId: string; token: string }) {
  const [state, action] = useActionState<ConfirmState, FormData>(confirmEmail, {});

  if (state.status === "confirmed") {
    return (
      <div role="status" className="space-y-4">
        <p className="text-success font-semibold">E-mail confirmado. Agora é só entrar.</p>
        <Link href="/entrar" className={buttonVariants()}>
          Entrar
        </Link>
      </div>
    );
  }
  return (
    <form action={action} className="space-y-4">
      <input type="hidden" name="conta" value={accountId} />
      <input type="hidden" name="token" value={token} />
      {state.status === "invalid" && (
        <p role="alert" className="text-danger font-medium">
          Este link venceu ou não vale mais (eles duram 1 dia). Entre com seu e-mail e senha para receber
          outro.
        </p>
      )}
      {state.status === "error" && (
        <p role="alert" className="text-danger font-medium">
          A confirmação não foi feita: a loja não respondeu. Tente de novo em instantes.
        </p>
      )}
      <SubmitButton pendingLabel="Confirmando…">Confirmar meu e-mail</SubmitButton>
    </form>
  );
}
