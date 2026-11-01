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

export type GuestResult = { ok: true; token: string } | { ok: false; status: number };

/**
 * Compra sem conta: abre a sessão de convidado com a chave do formulário. Dois envios ao mesmo tempo dão 409
 * em um deles, e a segunda tentativa acha a compra que o outro criou, sem duplicar o pedido.
 */
export async function startGuest(email: string, name: string, checkoutKey: string): Promise<GuestResult> {
  const body = { email, name, checkout_key: checkoutKey };
  try {
    let result = await api.identity().POST("/v1/sessions/guest", { body });
    if (result.response.status === 409) result = await api.identity().POST("/v1/sessions/guest", { body });
    if (!result.data) return { ok: false, status: result.response.status };
    const store = await cookies();
    for (const c of sessionCookies(result.data)) store.set(c.name, c.value, c.options);
    return { ok: true, token: result.data.access_token };
  } catch {
    return { ok: false, status: 503 };
  }
}

/** Abre a sessão pelo link do e-mail. Falso quando o link não vale, ou o identity não respondeu. */
export async function openGuestAccess(token: string): Promise<boolean> {
  try {
    const { data } = await api.identity().POST("/v1/sessions/guest/access", { body: { token } });
    if (!data) return false;
    const store = await cookies();
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    return true;
  } catch {
    return false;
  }
}

/** Pede o link de novo; devolve o status da resposta. */
export async function resendGuestAccess(email: string): Promise<number> {
  try {
    const { response } = await api.identity().POST("/v1/sessions/guest/access/resend", { body: { email } });
    return response.status;
  } catch {
    return 503;
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
