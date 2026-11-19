"use server";

import { api } from "@/lib/api/client";
import { requireAdmin } from "@/lib/auth/current";
import { normalizeOtp } from "@/lib/auth/otp";

export type RegenerateState = { codes?: string[]; error?: string };

/** Códigos de recuperação novos, com um código atual do app autenticador; os anteriores deixam de valer. */
export async function regenerateRecoveryCodes(_: RegenerateState, form: FormData): Promise<RegenerateState> {
  const { token } = await requireAdmin("/admin/seguranca");
  const code = normalizeOtp(form.get("code"));
  if (!code) return { error: "Digite os 6 números que o app autenticador mostra agora." };

  const { data, response } = await api
    .identity({ token })
    .POST("/v1/me/two-factor/recovery-codes", { body: { code } })
    .catch(() => ({ data: undefined, response: new Response(null, { status: 503 }) }));
  if (data) return { codes: data.recovery_codes };
  switch (response.status) {
    case 422:
      return { error: "Código incorreto. Digite o código que o app autenticador mostra agora." };
    case 403:
      return { error: "Saia e entre de novo com o código do app autenticador para gerar códigos novos." };
    case 423:
      return { error: "Muitas tentativas erradas seguidas. Espere 15 minutos e tente de novo." };
    default:
      return { error: "A loja não conseguiu gerar os códigos agora. Tente de novo em instantes." };
  }
}
