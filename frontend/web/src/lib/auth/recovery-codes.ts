import "server-only";
import { z } from "zod";
import { env } from "@/lib/env";
import { decodeCookie, encodeCookie } from "./google";
import { safeReturnTo } from "./return-to";

/**
 * Os códigos de recuperação logo depois de ativar a verificação em duas etapas. A ação que ativa também abre a
 * sessão (grava cookies), e o Next redesenha a página: os códigos não sobreviveriam no estado do formulário.
 * Vão num cookie httpOnly de 10 minutos, só da tela que os mostra; o "Continuar" o apaga.
 */
export const RECOVERY_CODES_COOKIE = "cf_mfa_codes";
export const RECOVERY_CODES_PATH = "/entrar/codigos-de-recuperacao";

const saved = z.object({ codes: z.array(z.string().max(32)).max(20), back: z.string() });

export function recoveryCodesCookie(codes: string[], back: unknown) {
  return {
    value: encodeCookie({ codes, back: safeReturnTo(back, "/admin") }),
    options: {
      httpOnly: true,
      secure: env.NODE_ENV === "production",
      sameSite: "lax" as const,
      path: RECOVERY_CODES_PATH,
      maxAge: 600,
    },
  };
}

export const readRecoveryCodes = (raw: string | undefined) => decodeCookie(raw, saved);
