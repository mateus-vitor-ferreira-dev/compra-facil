import type { Metadata } from "next";
import Link from "next/link";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { MFA_COOKIE, MFA_SETUP_PATH, readPendingMfa } from "@/lib/auth/mfa";
import { MfaForm } from "./mfa-form";

export const metadata: Metadata = { title: "Verificação em duas etapas", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/**
 * O segundo passo do login da conta com autenticação em duas etapas (a equipe da loja): a senha (ou o
 * Google) já conferiu, falta o código do app autenticador. Sem o app, um código de recuperação.
 */
export default async function MfaPage({ searchParams }: Props) {
  const pending = readPendingMfa((await cookies()).get(MFA_COOKIE)?.value);
  if (!pending) redirect("/entrar?erro=verificacao-expirada");
  if (pending.status === "mfa_setup_required") redirect(MFA_SETUP_PATH);
  const recovery = (await searchParams).recuperacao === "1";

  return (
    <AuthShell
      title="Verificação em duas etapas"
      intro={
        recovery
          ? "Sem o celular? Digite um dos códigos de recuperação que você guardou quando ativou a verificação. Cada código vale uma vez."
          : "Abra o app autenticador no celular e digite o código de 6 números da Embrulhado."
      }
    >
      <MfaForm recovery={recovery} />
      <p className="text-ink-soft">
        {recovery ? (
          <Link
            href="/entrar/verificacao"
            className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
          >
            Usar o código do app autenticador
          </Link>
        ) : (
          <>
            Sem acesso ao app?{" "}
            <Link
              href="/entrar/verificacao?recuperacao=1"
              className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
            >
              Usar um código de recuperação
            </Link>
          </>
        )}
      </p>
    </AuthShell>
  );
}
