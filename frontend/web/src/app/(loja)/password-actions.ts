"use server";

import { z } from "zod";
import { api } from "@/lib/api/client";
import { signOut } from "@/lib/auth/flows";
import { newPasswordProblems } from "@/lib/auth/password-rules";

/**
 * Esqueci minha senha: pedir o link e criar a senha nova com ele. Server actions de formulários comuns
 * (funcionam sem JavaScript), como as de entrar e criar conta.
 */

export type ForgotState = {
  email?: string;
  sent?: boolean;
  /** o e-mail digitado não é um e-mail */
  field?: string;
  /** a loja não mandou o link (fora do ar, muitos pedidos) */
  error?: string;
};

/** Pede o link. O identity responde igual exista a conta ou não: a tela também. */
export async function requestPasswordReset(_: ForgotState, form: FormData): Promise<ForgotState> {
  const email = String(form.get("email") ?? "").trim();
  const parsed = z.email().max(254).safeParse(email);
  if (!parsed.success) return { email, field: "Confira o e-mail: falta o @ ou o domínio." };
  try {
    const r = await api.identity().POST("/v1/accounts/password-reset", { body: { email: parsed.data } });
    if (r.response.status === 429)
      return { email, error: "Muitos pedidos de link em pouco tempo. Espere alguns minutos." };
    return r.response.ok
      ? { email, sent: true }
      : { email, error: "O link não foi enviado agora. Tente de novo em instantes." };
  } catch {
    return { email, error: "O link não foi enviado agora. Tente de novo em instantes." };
  }
}

export type ResetState = {
  status?: "done" | "invalid" | "error";
  fields?: Partial<Record<"password" | "confirm", string>>;
  error?: string;
};

/** Cria a senha nova pelo link (?conta=&token=). */
export async function resetPassword(_: ResetState, form: FormData): Promise<ResetState> {
  const accountId = z.uuid().safeParse(form.get("conta"));
  const token = z.string().min(1).max(2048).safeParse(form.get("token"));
  if (!accountId.success || !token.success) return { status: "invalid" };

  const password = String(form.get("password") ?? "");
  const fields = newPasswordProblems(password, String(form.get("confirm") ?? ""));
  if (fields.password || fields.confirm) return { fields };

  try {
    const r = await api.identity().POST("/v1/accounts/password-reset/confirm", {
      body: { account_id: accountId.data, token: token.data, password },
    });
    if (r.response.ok) {
      // o identity derrubou todas as sessões da conta: este navegador também sai, e entra com a senha nova
      await signOut().catch(() => undefined); // a senha já foi trocada: o resultado não depende disto
      return { status: "done" };
    }
    if (r.response.status === 422) {
      const errors = (r.error as { errors?: { field: string }[] } | undefined)?.errors ?? [];
      // só a senha fora das regras devolve o campo password; o resto é o link
      return errors.some((e) => e.field.toLowerCase() === "password")
        ? { fields: { password: "Escolha outra senha: de 8 a 128 caracteres, com pelo menos 4 diferentes." } }
        : { status: "invalid" };
    }
    if (r.response.status === 429)
      return { status: "error", error: "Muitas tentativas em pouco tempo. Espere um pouco e tente de novo." };
    return {
      status: "error",
      error: "A senha não foi trocada: a loja não respondeu. Tente de novo em instantes.",
    };
  } catch {
    return {
      status: "error",
      error: "A senha não foi trocada: a loja não respondeu. Tente de novo em instantes.",
    };
  }
}
