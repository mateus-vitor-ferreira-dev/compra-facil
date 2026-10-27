import "server-only";
import { createHash, randomBytes } from "node:crypto";
import { z } from "zod";
import { env } from "@/lib/env";

/**
 * Login com o Google no BFF (#180): authorization code com PKCE. O navegador vai ao Google e volta ao
 * callback com um código; o BFF troca o código pelo ID token (com o client secret e o code_verifier,
 * que nunca saem do servidor) e o entrega ao identity, que valida e abre a sessão. O state (contra
 * CSRF no callback), o nonce (o ID token é deste login) e o verifier ficam num cookie httpOnly de 10
 * minutos, só no caminho do callback.
 */
export const GOOGLE_COOKIE = "cf_google";
/** O ID token de um login que precisa da senha para vincular (o e-mail já tinha conta). */
export const GOOGLE_LINK_COOKIE = "cf_google_link";
export const GOOGLE_COOKIE_MAX_AGE = 600;

export const googleEnabled = () => Boolean(env.GOOGLE_CLIENT_ID && env.GOOGLE_CLIENT_SECRET);

const random = (bytes = 32) => randomBytes(bytes).toString("base64url");

/** RFC 7636: o challenge S256 é o SHA-256 do verifier, em base64url sem padding. */
export const codeChallenge = (verifier: string) => createHash("sha256").update(verifier).digest("base64url");

export const redirectUri = () => `${env.SITE_URL.replace(/\/$/, "")}/api/auth/google/callback`;

const pending = z.object({ state: z.string(), verifier: z.string(), nonce: z.string(), back: z.string() });
export type PendingLogin = z.infer<typeof pending>;

/** Um login novo: o que vai no cookie e o endereço do Google para onde o navegador vai. */
export function startLogin(back: string): { pending: PendingLogin; url: string } {
  const login: PendingLogin = { state: random(), verifier: random(48), nonce: random(), back };
  const url = new URL(env.GOOGLE_AUTH_URL);
  url.search = new URLSearchParams({
    client_id: env.GOOGLE_CLIENT_ID ?? "",
    redirect_uri: redirectUri(),
    response_type: "code",
    scope: "openid email profile",
    state: login.state,
    nonce: login.nonce,
    code_challenge: codeChallenge(login.verifier),
    code_challenge_method: "S256",
    // sempre a escolha da conta: quem tem várias contas Google escolhe qual usar
    prompt: "select_account",
  }).toString();
  return { pending: login, url: url.toString() };
}

export const encodeCookie = (value: unknown) => Buffer.from(JSON.stringify(value)).toString("base64url");

function decodeCookie<T>(raw: string | undefined, schema: z.ZodType<T>): T | undefined {
  if (!raw) return undefined;
  try {
    const parsed = schema.safeParse(JSON.parse(Buffer.from(raw, "base64url").toString("utf8")));
    return parsed.success ? parsed.data : undefined;
  } catch {
    return undefined;
  }
}

export const readPending = (raw: string | undefined) => decodeCookie(raw, pending);

const link = z.object({ idToken: z.string(), nonce: z.string(), email: z.string(), back: z.string() });
export type PendingLink = z.infer<typeof link>;
export const readPendingLink = (raw: string | undefined) => decodeCookie(raw, link);

/** O e-mail de dentro do ID token, só para mostrar na tela do vínculo (quem valida é o identity). */
export function emailOf(idToken: string): string {
  try {
    const payload = JSON.parse(Buffer.from(idToken.split(".")[1] ?? "", "base64url").toString("utf8"));
    return typeof payload.email === "string" ? payload.email : "";
  } catch {
    return "";
  }
}

/** Troca o código do callback pelo ID token no Google. */
export async function exchangeCode(code: string, verifier: string): Promise<string | undefined> {
  try {
    const response = await fetch(env.GOOGLE_TOKEN_URL, {
      method: "POST",
      headers: { "Content-Type": "application/x-www-form-urlencoded" },
      body: new URLSearchParams({
        code,
        code_verifier: verifier,
        client_id: env.GOOGLE_CLIENT_ID ?? "",
        client_secret: env.GOOGLE_CLIENT_SECRET ?? "",
        redirect_uri: redirectUri(),
        grant_type: "authorization_code",
      }),
      cache: "no-store",
    });
    if (!response.ok) return undefined;
    const body = (await response.json()) as { id_token?: unknown };
    return typeof body.id_token === "string" ? body.id_token : undefined;
  } catch {
    return undefined;
  }
}
