import "server-only";
import { env } from "@/lib/env";
import type { IdentitySchemas } from "@/lib/api/client";

/**
 * A sessão do cliente no BFF: o access token (JWT, 15 min) e o refresh token (opaco, 30 dias) ficam em
 * cookies httpOnly, nunca acessíveis ao JavaScript do navegador nem no localStorage.
 */
export const ACCESS_COOKIE = "cf_access";
export const REFRESH_COOKIE = "cf_refresh";

/** Folga antes do vencimento: o access token é renovado 1 min antes de vencer. */
export const ACCESS_SKEW_SECONDS = 60;

export type Tokens = IdentitySchemas["schemas"]["Tokens"];

export type CookieOptions = {
  httpOnly: true;
  secure: boolean;
  sameSite: "lax";
  path: "/";
  maxAge: number;
};

function options(maxAge: number): CookieOptions {
  // Lax: o cookie vai na navegação vinda de outro site (o link do e-mail), não em POST de outro site
  return { httpOnly: true, secure: env.NODE_ENV === "production", sameSite: "lax", path: "/", maxAge };
}

/** Os dois cookies de uma sessão nova (login ou refresh). */
export function sessionCookies(
  tokens: Tokens,
): Array<{ name: string; value: string; options: CookieOptions }> {
  return [
    {
      name: ACCESS_COOKIE,
      value: tokens.access_token,
      options: options(Math.max(tokens.expires_in - ACCESS_SKEW_SECONDS, 1)),
    },
    { name: REFRESH_COOKIE, value: tokens.refresh_token, options: options(tokens.refresh_expires_in) },
  ];
}

/** Apaga os dois cookies (logout ou sessão derrubada). */
export function clearedCookies(): Array<{ name: string; value: string; options: CookieOptions }> {
  return [ACCESS_COOKIE, REFRESH_COOKIE].map((name) => ({ name, value: "", options: options(0) }));
}

export type RefreshResult = { ok: true; tokens: Tokens } | { ok: false; status: number };

// Um refresh por vez para cada refresh token, nesta instância: o navegador costuma disparar várias
// requisições juntas (página, prefetch, imagens), e uma troca basta. Entre instâncias (os isolates do
// Workers), o intervalo de reuso do identity (10 s) faz o refresh paralelo dar um par novo da mesma sessão.
const inFlight = new Map<string, Promise<RefreshResult>>();

export function refreshSession(
  refreshToken: string,
  fetcher: typeof fetch = fetch,
  extraHeaders: Record<string, string> = {},
): Promise<RefreshResult> {
  const running = inFlight.get(refreshToken);
  if (running) return running;
  const call = (async (): Promise<RefreshResult> => {
    const response = await fetcher(`${env.API_URL.replace(/\/$/, "")}/identity/v1/sessions/refresh`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...extraHeaders },
      body: JSON.stringify({ refresh_token: refreshToken }),
      cache: "no-store",
    });
    if (!response.ok) return { ok: false, status: response.status };
    return { ok: true, tokens: (await response.json()) as Tokens };
  })().finally(() => {
    // o resultado fica alguns segundos para as requisições que chegaram junto
    setTimeout(() => inFlight.delete(refreshToken), 5_000).unref?.();
  });
  inFlight.set(refreshToken, call);
  return call;
}
