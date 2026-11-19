"use server";

import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { api } from "@/lib/api/client";
import { GOOGLE_LINK_COOKIE, readPendingLink } from "@/lib/auth/google";
import { isChallenge, mfaPath, pendingMfaCookie } from "@/lib/auth/mfa";
import { safeReturnTo } from "@/lib/auth/return-to";
import { sessionCookies } from "@/lib/auth/session";

export type LinkState = { error?: string };

/** Confere a senha da conta e liga o Google a ela; daí em diante o Google sozinho entra. */
export async function linkGoogle(_: LinkState, form: FormData): Promise<LinkState> {
  const store = await cookies();
  const pending = readPendingLink(store.get(GOOGLE_LINK_COOKIE)?.value);
  if (!pending) redirect("/entrar?erro=google-expirado");
  const password = String(form.get("password") ?? "");
  if (!password) return { error: "Digite a senha da conta." };

  const { data, response } = await api
    .identity()
    .POST("/v1/sessions/google/link", { body: { id_token: pending.idToken, nonce: pending.nonce, password } })
    .catch(() => ({ data: undefined, response: new Response(null, { status: 503 }) }));
  if (data) {
    store.delete({ name: GOOGLE_LINK_COOKIE, path: "/entrar" });
    // conta com 2FA: a senha e o Google não bastam, falta o código
    if (isChallenge(data)) {
      const c = pendingMfaCookie(data, pending.back);
      store.set(c.name, c.value, c.options);
      redirect(mfaPath(data));
    }
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    redirect(safeReturnTo(pending.back));
  }
  switch (response.status) {
    case 401:
      return { error: "Senha incorreta. É a senha desta conta na loja, não a do Google." };
    case 423:
      return { error: "Muitas senhas erradas seguidas. A conta volta a aceitar a senha em 15 minutos." };
    default:
      return { error: "A loja não conseguiu vincular agora. Tente de novo em instantes." };
  }
}
