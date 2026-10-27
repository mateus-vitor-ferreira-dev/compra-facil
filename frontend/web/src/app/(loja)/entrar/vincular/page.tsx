import type { Metadata } from "next";
import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { GOOGLE_LINK_COOKIE, readPendingLink } from "@/lib/auth/google";
import { LinkForm } from "./link-form";

export const metadata: Metadata = { title: "Vincular o Google", robots: { index: false } };

/**
 * O e-mail do Google já tem conta com senha: para o Google também entrar nela, a pessoa confirma a
 * senha uma vez. Sem isso, quem controlasse um Google com aquele e-mail entraria na conta de outra.
 */
export default async function LinkGooglePage() {
  const pending = readPendingLink((await cookies()).get(GOOGLE_LINK_COOKIE)?.value);
  if (!pending) redirect("/entrar?erro=google-expirado");
  return (
    <AuthShell
      title="Vincular a conta Google"
      intro={`Já existe uma conta com ${pending.email || "este e-mail"}. Digite a senha dela uma vez; depois, o Google sozinho entra nela.`}
    >
      <LinkForm email={pending.email} />
      <p className="text-ink-soft">
        Não lembra a senha?{" "}
        <Link
          href="/entrar"
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Entrar com e-mail e senha
        </Link>
      </p>
    </AuthShell>
  );
}
