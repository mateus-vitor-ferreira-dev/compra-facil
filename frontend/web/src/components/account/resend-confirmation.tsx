"use client";

import { useActionState } from "react";
import { SubmitButton } from "@/components/ui/submit-button";
import { resendConfirmation, type ResendState } from "@/app/(loja)/auth-actions";

/** "Mandar o link de novo", para quem não achou o e-mail de confirmação. */
export function ResendConfirmation({ email }: { email: string }) {
  const [state, action] = useActionState<ResendState, FormData>(resendConfirmation, {});
  return (
    <form action={action} className="space-y-2">
      <input type="hidden" name="email" value={email} />
      <SubmitButton variant="secondary" size="sm" pendingLabel="Enviando…" disabled={state.sent}>
        {state.sent ? "Link enviado" : "Mandar o link de novo"}
      </SubmitButton>
      <p aria-live="polite" className="text-sm">
        {state.sent && `Se ${email} tiver conta, o link chega em instantes. Confira também o spam.`}
        {state.error && <span className="text-danger font-medium">{state.error}</span>}
      </p>
    </form>
  );
}
