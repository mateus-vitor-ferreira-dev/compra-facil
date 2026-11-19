import "server-only";
import { cookies } from "next/headers";
import { api } from "@/lib/api/client";
import { maskEmail } from "@/lib/guest";
import { currentAccount } from "./current";
import { isChallenge, mfaPath, pendingMfaCookie } from "./mfa";
import { REFRESH_COOKIE, clearedCookies, sessionCookies } from "./session";

export type SignInResult =
  | { ok: true }
  | { ok: false; status: number; retryAfter?: number }
  /** a senha conferiu, mas a conta pede o segundo fator: a tela do código (ou do cadastro do autenticador) */
  | { ok: "mfa"; path: string };

/**
 * Login no identity e os tokens nos cookies httpOnly. Só em server action (onde o Next deixa gravar
 * cookie). As rotas /api/auth/* fazem o mesmo para quem chama por JSON. Conta com 2FA: guarda o tíquete
 * do segundo passo (e o destino `back`) e diz para qual tela ir.
 */
export async function signIn(email: string, password: string, back?: unknown): Promise<SignInResult> {
  try {
    const { data, response } = await api.identity().POST("/v1/sessions", { body: { email, password } });
    if (!data) {
      const retry = Number(response.headers.get("retry-after"));
      return { ok: false, status: response.status, retryAfter: retry > 0 ? retry : undefined };
    }
    const store = await cookies();
    if (isChallenge(data)) {
      const c = pendingMfaCookie(data, back);
      store.set(c.name, c.value, c.options);
      return { ok: "mfa", path: mfaPath(data) };
    }
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

export type GuestAccessResult =
  | { ok: true }
  | { ok: false; reason: "invalid" }
  /** o navegador já tem outra sessão: a conta com senha (`account`) ou outra compra sem conta (`guest`) */
  | { ok: false; reason: "other"; current: "account" | "guest"; email: string };

/**
 * Abre a sessão pelo link do e-mail. Com outra sessão neste navegador, não a troca sozinho: um link de outra
 * pessoa (encaminhado, ou mandado de propósito) não pode tirar o comprador da conta dele e pô-lo numa compra
 * alheia sem ele ver. Nesse caso a sessão do link é revogada e volta `other`, com o e-mail dela mascarado; o
 * comprador confirma e chama de novo com `replace` (aí a sessão de antes sai). O link vale até vencer, então
 * pedir de novo funciona.
 */
export async function openGuestAccess(token: string, replace = false): Promise<GuestAccessResult> {
  try {
    const { data } = await api.identity().POST("/v1/sessions/guest/access", { body: { token } });
    if (!data) return { ok: false, reason: "invalid" };
    const store = await cookies();
    if (!replace && store.has(REFRESH_COOKIE)) {
      const current = await currentAccount();
      const { data: linked } = await api
        .identity({ token: data.access_token })
        .GET("/v1/me", { cache: "no-store" });
      if (!linked || linked.id !== current?.id) {
        await revoke(data.refresh_token);
        if (!linked) return { ok: false, reason: "invalid" };
        // sessão que não se lê (vencida, revogada) conta como conta: o comprador decide sair
        return {
          ok: false,
          reason: "other",
          current: current?.is_guest ? "guest" : "account",
          email: maskEmail(linked.email),
        };
      }
    }
    if (replace) await signOut();
    for (const c of sessionCookies(data)) store.set(c.name, c.value, c.options);
    return { ok: true };
  } catch {
    return { ok: false, reason: "invalid" };
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
  if (refresh) await revoke(refresh);
  for (const c of clearedCookies()) store.set(c.name, c.value, c.options);
}

function revoke(refreshToken: string): Promise<unknown> {
  return api
    .identity()
    .POST("/v1/sessions/revoke", { body: { refresh_token: refreshToken } })
    .catch(() => undefined);
}

/** Há sessão (access ou refresh)? Sem chamar o identity: serve para o cabeçalho trocar "Entrar" por "Conta". */
export async function hasSession(): Promise<boolean> {
  const store = await cookies();
  return store.has(REFRESH_COOKIE);
}
