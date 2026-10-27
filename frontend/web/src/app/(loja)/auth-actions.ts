"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { api } from "@/lib/api/client";
import { signIn, signOut } from "@/lib/auth/flows";
import { safeReturnTo } from "@/lib/auth/return-to";

/**
 * Entrar, criar conta, sair e confirmar o e-mail. Server actions chamadas por formulários comuns
 * (funcionam sem JavaScript); os tokens vão para cookies httpOnly, nunca para o navegador.
 */

export type LoginState = {
  email?: string;
  error?: string;
  /** senha certa, e-mail ainda não confirmado: a tela oferece mandar o link de novo */
  unconfirmed?: boolean;
};

const minutes = (s?: number) => Math.max(1, Math.ceil((s ?? 900) / 60));

export async function login(_: LoginState, form: FormData): Promise<LoginState> {
  const email = String(form.get("email") ?? "").trim();
  const password = String(form.get("password") ?? "");
  if (!email || !password) return { email, error: "Preencha o e-mail e a senha." };

  const result = await signIn(email, password);
  if (result.ok) redirect(safeReturnTo(form.get("voltar")));
  switch (result.status) {
    case 401:
    case 422:
      return { email, error: "E-mail ou senha incorretos." };
    case 403:
      return {
        email,
        unconfirmed: true,
        error: "Confirme seu e-mail para entrar: o link foi para a sua caixa de entrada.",
      };
    case 423:
      return {
        email,
        error: `Muitas senhas erradas seguidas. A conta volta a aceitar login em ${minutes(result.retryAfter)} min.`,
      };
    case 429:
      return {
        email,
        error: `Muitas tentativas em pouco tempo. Espere ${result.retryAfter ?? 30} segundos.`,
      };
    default:
      return { email, error: "A loja não conseguiu fazer o login agora. Tente de novo em instantes." };
  }
}

export type RegisterState = {
  values?: { name: string; email: string };
  fields?: Partial<Record<"name" | "email" | "password", string>>;
  error?: string;
  /** conta criada, mas a loja exige confirmar o e-mail antes do primeiro login */
  confirmTo?: string;
};

const registerSchema = z.object({
  name: z.string().trim().min(2, "Escreva seu nome.").max(120, "Use até 120 caracteres."),
  email: z
    .string()
    .min(1, "Escreva seu e-mail.")
    .pipe(z.email("Confira o e-mail: falta o @ ou o domínio.").max(254)),
  password: z
    .string()
    .min(8, "A senha precisa de pelo menos 8 caracteres.")
    .max(128, "Use até 128 caracteres.")
    .refine((p) => new Set(p).size >= 4, "Use pelo menos 4 caracteres diferentes."),
});

// os textos do identity são em inglês: o front fala com o cliente em português, campo a campo
const serverFieldMessage: Record<string, string> = {
  name: "Confira o nome.",
  email: "Confira o e-mail.",
  password: "Escolha outra senha: de 8 a 128 caracteres, com pelo menos 4 diferentes.",
};

export async function register(_: RegisterState, form: FormData): Promise<RegisterState> {
  const raw = {
    name: String(form.get("name") ?? ""),
    email: String(form.get("email") ?? "").trim(),
    password: String(form.get("password") ?? ""),
  };
  const values = { name: raw.name, email: raw.email };
  const parsed = registerSchema.safeParse(raw);
  if (!parsed.success) {
    const fields: RegisterState["fields"] = {};
    for (const issue of parsed.error.issues) {
      const key = issue.path[0] as keyof NonNullable<RegisterState["fields"]>;
      fields[key] ??= issue.message;
    }
    return { values, fields };
  }

  try {
    const created = await api.identity().POST("/v1/accounts", { body: parsed.data });
    if (!created.data) {
      const status = created.response.status;
      if (status === 409) {
        return { values, fields: { email: "Já existe uma conta com este e-mail. Entre com a sua senha." } };
      }
      if (status === 422) {
        const errors = (created.error as { errors?: { field: string }[] } | undefined)?.errors ?? [];
        const fields: RegisterState["fields"] = {};
        for (const e of errors) {
          const key = e.field.toLowerCase() as keyof NonNullable<RegisterState["fields"]>;
          if (key in serverFieldMessage) fields[key] = serverFieldMessage[key];
        }
        return {
          values,
          fields,
          error: Object.keys(fields).length ? undefined : "Confira os dados do cadastro.",
        };
      }
      if (status === 429)
        return { values, error: "Muitos cadastros em pouco tempo. Espere um pouco e tente de novo." };
      return { values, error: "A conta não foi criada: a loja não respondeu. Tente de novo em instantes." };
    }
  } catch {
    return { values, error: "A conta não foi criada: a loja não respondeu. Tente de novo em instantes." };
  }

  const session = await signIn(parsed.data.email, parsed.data.password);
  if (session.ok) redirect(safeReturnTo(form.get("voltar")));
  if (session.status === 403) return { confirmTo: parsed.data.email };
  // a conta existe; só o login automático falhou
  redirect(`/entrar?criada=1&voltar=${encodeURIComponent(safeReturnTo(form.get("voltar")))}`);
}

export async function logout(): Promise<void> {
  await signOut();
  redirect("/");
}

export type ResendState = { sent?: boolean; error?: string };

/** Manda outro link de confirmação. O identity responde igual exista a conta ou não. */
export async function resendConfirmation(_: ResendState, form: FormData): Promise<ResendState> {
  const email = z.email().safeParse(String(form.get("email") ?? "").trim());
  if (!email.success) return { error: "Confira o e-mail." };
  try {
    const r = await api.identity().POST("/v1/accounts/confirm-email/resend", { body: { email: email.data } });
    if (r.response.status === 429) return { error: "Muitos pedidos de link. Espere alguns minutos." };
    return r.response.ok
      ? { sent: true }
      : { error: "O link não foi enviado agora. Tente de novo em instantes." };
  } catch {
    return { error: "O link não foi enviado agora. Tente de novo em instantes." };
  }
}

export type ConfirmState = { status?: "confirmed" | "invalid" | "error" };

export async function confirmEmail(_: ConfirmState, form: FormData): Promise<ConfirmState> {
  const accountId = z.uuid().safeParse(form.get("conta"));
  const token = z.string().min(1).max(2048).safeParse(form.get("token"));
  if (!accountId.success || !token.success) return { status: "invalid" };
  try {
    const r = await api.identity().POST("/v1/accounts/confirm-email", {
      body: { account_id: accountId.data, token: token.data },
    });
    if (r.response.ok) return { status: "confirmed" };
    return { status: r.response.status === 422 ? "invalid" : "error" };
  } catch {
    return { status: "error" };
  }
}
