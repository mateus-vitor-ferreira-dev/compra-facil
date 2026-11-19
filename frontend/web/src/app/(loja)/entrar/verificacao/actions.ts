"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api } from "@/lib/api/client";
import { MFA_COOKIE, clearedMfaCookie, readPendingMfa } from "@/lib/auth/mfa";
import { safeReturnTo } from "@/lib/auth/return-to";
import { sessionCookies } from "@/lib/auth/session";
import { normalizeOtp, normalizeRecoveryCode } from "@/lib/auth/otp";

export type MfaState = { error?: string };

const EXPIRED = "/entrar?erro=verificacao-expirada";

/**
 * O segundo passo do login: o código do app autenticador (ou um de recuperação) troca o tíquete guardado no
 * cookie pela sessão. Tíquete vencido ou já usado: volta para a senha.
 */
export async function verifyMfa(_: MfaState, form: FormData): Promise<MfaState> {
  const store = await cookies();
  const pending = readPendingMfa(store.get(MFA_COOKIE)?.value);
  if (!pending || pending.status !== "mfa_required") redirect(EXPIRED);

  const recovery = form.get("modo") === "recuperacao";
  const code = recovery ? normalizeRecoveryCode(form.get("recovery_code")) : normalizeOtp(form.get("code"));
  if (!code) {
    return {
      error: recovery
        ? "Digite um dos códigos de recuperação, com 10 letras e números (ex.: k7m2p-9xq4r)."
        : "Digite os 6 números que o app autenticador mostra agora.",
    };
  }

  const body = recovery ? { ticket: pending.ticket, recovery_code: code } : { ticket: pending.ticket, code };
  const { data, response } = await api
    .identity()
    .POST("/v1/sessions/mfa", { body })
    .catch(() => ({ data: undefined, response: new Response(null, { status: 503 }) }));
  if (data) {
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    store.delete(clearedMfaCookie);
    redirect(safeReturnTo(pending.back));
  }
  switch (response.status) {
    case 401:
      store.delete(clearedMfaCookie);
      redirect(EXPIRED);
    case 422:
      return {
        error: recovery
          ? "Código de recuperação incorreto ou já usado. Cada um vale uma vez."
          : "Código incorreto. Confira o app autenticador e digite o código que aparece agora.",
      };
    case 423: {
      const retry = Number(response.headers.get("retry-after"));
      const minutes = Math.max(1, Math.ceil((retry > 0 ? retry : 900) / 60));
      return {
        error: `Muitas tentativas erradas seguidas. A conta volta a aceitar o login em ${minutes} min.`,
      };
    }
    case 429:
      return { error: "Muitas tentativas em pouco tempo. Espere alguns segundos." };
    default:
      return { error: "A loja não conseguiu conferir o código agora. Tente de novo em instantes." };
  }
}
