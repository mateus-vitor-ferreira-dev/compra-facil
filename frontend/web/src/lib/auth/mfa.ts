import "server-only";
import { z } from "zod";
import type { IdentitySchemas } from "@/lib/api/client";
import { env } from "@/lib/env";
import { decodeCookie, encodeCookie } from "./google";
import { safeReturnTo } from "./return-to";
import type { CookieOptions, Tokens } from "./session";

/**
 * O segundo fator do login (ameaça T8). Conta com 2FA (todo operador) não recebe tokens só com a senha ou
 * com o Google: o identity responde 202 com um tíquete curto, de uso único. O tíquete fica num cookie
 * httpOnly só das telas de entrada, nunca na URL nem no JavaScript, junto com o destino depois do login.
 */
export const MFA_COOKIE = "cf_mfa";

export type MfaChallenge = IdentitySchemas["schemas"]["MfaChallenge"];

/** A tela do segundo passo: o código (2FA ativa) ou o cadastro do app autenticador (operador sem 2FA). */
export const MFA_VERIFY_PATH = "/entrar/verificacao";
export const MFA_SETUP_PATH = "/entrar/autenticador";

const pending = z.object({
  ticket: z.string().min(1).max(128),
  status: z.enum(["mfa_required", "mfa_setup_required"]),
  back: z.string(),
});
export type PendingMfa = z.infer<typeof pending>;

/** A resposta do login é o desafio do segundo fator (202), não os tokens. */
export function isChallenge(body: Tokens | MfaChallenge | undefined): body is MfaChallenge {
  return Boolean(body && "mfa_ticket" in body);
}

export const mfaPath = (challenge: Pick<MfaChallenge, "status">) =>
  challenge.status === "mfa_setup_required" ? MFA_SETUP_PATH : MFA_VERIFY_PATH;

/** O cookie do tíquete: vence junto com ele e só vai para as telas de entrada. */
export function pendingMfaCookie(
  challenge: MfaChallenge,
  back: unknown,
): { name: string; value: string; options: Omit<CookieOptions, "path"> & { path: "/entrar" } } {
  const value: PendingMfa = {
    ticket: challenge.mfa_ticket,
    status: challenge.status,
    back: safeReturnTo(back),
  };
  return {
    name: MFA_COOKIE,
    value: encodeCookie(value),
    options: {
      httpOnly: true,
      secure: env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/entrar",
      maxAge: Math.max(challenge.expires_in, 1),
    },
  };
}

export const readPendingMfa = (raw: string | undefined) => decodeCookie(raw, pending);

/** Apaga o cookie do tíquete (gasto, vencido ou recusado). */
export const clearedMfaCookie = { name: MFA_COOKIE, path: "/entrar" } as const;
