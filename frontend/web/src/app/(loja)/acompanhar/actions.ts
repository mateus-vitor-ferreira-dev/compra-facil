"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { openGuestAccess, resendGuestAccess } from "@/lib/auth/flows";
import { checkGuestEmail } from "@/lib/guest";

export type ResendState = { email?: string; error?: string; sent?: boolean };

/** Manda o link de novo. A resposta é a mesma com compra ou sem compra naquele e-mail. */
export async function requestGuestLink(_: ResendState, form: FormData): Promise<ResendState> {
  const email = String(form.get("email") ?? "").trim();
  const error = checkGuestEmail(email);
  if (error) return { email, error };
  const status = await resendGuestAccess(email);
  if (status === 429)
    return { email, error: "Muitas tentativas seguidas. Espere alguns minutos e tente de novo." };
  if (status >= 500)
    return { email, error: "Não conseguimos pedir o link agora. Tente de novo em instantes." };
  return { email, sent: true };
}

/** Abre a sessão pelo link do e-mail, no clique: abrir o link sozinho (um antivírus de e-mail) não entra. */
export async function openGuestLink(form: FormData): Promise<void> {
  const token = z.string().min(1).max(128).safeParse(form.get("token"));
  const opened = token.success && (await openGuestAccess(token.data));
  redirect(opened ? "/conta" : "/acompanhar?erro=link");
}
