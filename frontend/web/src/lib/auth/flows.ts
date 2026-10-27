import "server-only";
import { cookies } from "next/headers";
import { api } from "@/lib/api/client";
import { REFRESH_COOKIE, clearedCookies, sessionCookies } from "./session";

export type SignInResult = { ok: true } | { ok: false; status: number; retryAfter?: number };

/**
 * Login no identity e os tokens nos cookies httpOnly. Só em server action (onde o Next deixa gravar
 * cookie). As rotas /api/auth/* fazem o mesmo para quem chama por JSON.
 */
export async function signIn(email: string, password: string): Promise<SignInResult> {
  try {
    const { data, response } = await api.identity().POST("/v1/sessions", { body: { email, password } });
    if (!data) {
      const retry = Number(response.headers.get("retry-after"));
      return { ok: false, status: response.status, retryAfter: retry > 0 ? retry : undefined };
    }
    const store = await cookies();
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    return { ok: true };
  } catch {
    return { ok: false, status: 503 };
  }
}

/** Revoga a sessão no identity (melhor esforço) e apaga os cookies. */
export async function signOut(): Promise<void> {
  const store = await cookies();
  const refresh = store.get(REFRESH_COOKIE)?.value;
  if (refresh) {
    await api
      .identity()
      .POST("/v1/sessions/revoke", { body: { refresh_token: refresh } })
      .catch(() => undefined);
  }
  for (const c of clearedCookies()) store.set(c.name, c.value, c.options);
}

/** Há sessão (access ou refresh)? Sem chamar o identity: serve para o cabeçalho trocar "Entrar" por "Conta". */
export async function hasSession(): Promise<boolean> {
  const store = await cookies();
  return store.has(REFRESH_COOKIE);
}
