"use client";

import { useActionState, useEffect, useRef } from "react";
import { CopyButton } from "@/components/ui/copy-button";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { confirmSetup, startSetup, type ConfirmState, type SetupState } from "./actions";

/**
 * Os dois passos do cadastro do app autenticador numa tela: gerar o QR code (com a chave para digitar, para
 * quem não consegue ler o QR) e confirmar com o primeiro código. A chave fica só no estado desta tela.
 */
export function SetupFlow() {
  const [setup, start] = useActionState<SetupState, FormData>(startSetup, {});
  const [confirm, send] = useActionState<ConfirmState, FormData>(confirmSetup, {});
  const form = useRef<HTMLFormElement>(null);
  useEffect(() => {
    if (confirm.error) form.current?.querySelector<HTMLInputElement>("[aria-invalid=true]")?.focus();
  }, [confirm]);

  return (
    <div className="space-y-6">
      <ol className="text-ink-soft list-decimal space-y-1 pl-5">
        <li>
          Instale um app autenticador no celular (Google Authenticator, Microsoft Authenticator, 1Password…).
        </li>
        <li>Gere o QR code abaixo e leia com o app.</li>
        <li>Digite o código de 6 números que o app mostrar.</li>
      </ol>

      {setup.error && (
        <p role="alert" className="text-danger font-medium">
          {setup.error}
        </p>
      )}

      {setup.key && setup.qr ? (
        <section aria-labelledby="qr-titulo" className="space-y-4">
          <h2 id="qr-titulo" className="text-lg font-semibold">
            Leia com o app autenticador
          </h2>
          <div
            role="img"
            aria-label="QR code para cadastrar a Embrulhado no app autenticador"
            className="size-56 rounded-md border bg-white p-1 [&>svg]:size-full"
            // o SVG vem do servidor (biblioteca qrcode), a partir do URI otpauth do identity
            dangerouslySetInnerHTML={{ __html: setup.qr }}
          />
          <div className="space-y-1">
            <p className="font-medium">Não consegue ler? Digite esta chave no app:</p>
            <div className="flex flex-wrap items-center gap-2">
              <code className="bg-surface rounded-md border px-3 py-2 font-mono text-base tracking-wider break-all">
                {setup.key}
              </code>
              <CopyButton value={setup.key.replace(/\s/g, "")} label="a chave" />
            </div>
          </div>
          <form ref={form} action={send} className="space-y-4" noValidate>
            <TextField
              label="Código do app autenticador"
              name="code"
              autoComplete="one-time-code"
              inputMode="numeric"
              pattern="[0-9 ]*"
              maxLength={7}
              hint="6 números; o código muda a cada 30 segundos"
              error={confirm.error}
              required
            />
            <SubmitButton className="w-full" pendingLabel="Ativando…">
              Ativar e entrar
            </SubmitButton>
          </form>
          <form action={start}>
            <SubmitButton variant="ghost" size="sm" pendingLabel="Gerando…">
              Gerar outro QR code
            </SubmitButton>
          </form>
        </section>
      ) : (
        <form action={start}>
          <SubmitButton className="w-full" pendingLabel="Gerando…">
            Gerar o QR code
          </SubmitButton>
        </form>
      )}
    </div>
  );
}
