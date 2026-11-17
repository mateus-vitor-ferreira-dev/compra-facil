"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api } from "@/lib/api/client";
import { MFA_COOKIE, clearedMfaCookie, readPendingMfa } from "@/lib/auth/mfa";
import { normalizeOtp } from "@/lib/auth/otp";
import { RECOVERY_CODES_COOKIE, RECOVERY_CODES_PATH, recoveryCodesCookie } from "@/lib/auth/recovery-codes";
import { sessionCookies } from "@/lib/auth/session";
import { qrSvg } from "@/lib/qr";

export type SetupState = {
  /** o QR code (SVG) e a chave para digitar no app, quando o cadastro começou */
  qr?: string;
  key?: string;
  error?: string;
};

export type ConfirmState = { error?: string };

const EXPIRED = "/entrar?erro=verificacao-expirada";

async function pendingSetup() {
  const store = await cookies();
  const pending = readPendingMfa(store.get(MFA_COOKIE)?.value);
  if (!pending || pending.status !== "mfa_setup_required") redirect(EXPIRED);
  return { store, pending };
}

/**
 * Cadastro do app autenticador, passo 1: a chave nova e o QR code. Cada clique gera outra chave (a anterior
 * deixa de servir): vale o QR code que está na tela.
 */
export async function startSetup(_: SetupState, _form: FormData): Promise<SetupState> {
  const { store, pending } = await pendingSetup();
  const { data, response } = await api
    .identity()
    .POST("/v1/sessions/mfa/setup", { body: { ticket: pending.ticket } })
    .catch(() => ({ data: undefined, response: new Response(null, { status: 503 }) }));
  if (data) return { qr: await qrSvg(data.authenticator_uri), key: data.shared_key };
  if (response.status === 401) {
    store.delete(clearedMfaCookie);
    redirect(EXPIRED);
  }
  if (response.status === 423)
    return { error: "Muitas tentativas erradas seguidas. Espere 15 minutos e entre de novo." };
  return { error: "A loja não conseguiu gerar o QR code agora. Tente de novo em instantes." };
}

/**
 * Passo 2: o primeiro código do app liga a verificação em duas etapas e abre a sessão. Os códigos de
 * recuperação vão para a tela seguinte (num cookie curto, só dela), onde aparecem uma única vez.
 */
export async function confirmSetup(_: ConfirmState, form: FormData): Promise<ConfirmState> {
  const { store, pending } = await pendingSetup();
  const code = normalizeOtp(form.get("code"));
  if (!code) return { error: "Digite os 6 números que o app autenticador mostra agora." };

  const { data, response } = await api
    .identity()
    .POST("/v1/sessions/mfa/setup/confirm", { body: { ticket: pending.ticket, code } })
    .catch(() => ({ data: undefined, response: new Response(null, { status: 503 }) }));
  if (data) {
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    store.delete(clearedMfaCookie);
    const codes = recoveryCodesCookie(data.recovery_codes, pending.back);
    store.set(RECOVERY_CODES_COOKIE, codes.value, codes.options);
    redirect(RECOVERY_CODES_PATH);
  }
  switch (response.status) {
    case 401:
      store.delete(clearedMfaCookie);
      redirect(EXPIRED);
    case 422:
      return {
        error:
          "Código incorreto. Confira se o app leu o QR code desta tela e digite o código que aparece agora.",
      };
    case 423:
      return { error: "Muitas tentativas erradas seguidas. Espere 15 minutos e entre de novo." };
    case 429:
      return { error: "Muitas tentativas em pouco tempo. Espere alguns segundos." };
    default:
      return { error: "A loja não conseguiu ativar agora. Tente de novo em instantes." };
  }
}
