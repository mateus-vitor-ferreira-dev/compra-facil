import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { hasSession } from "@/lib/auth/flows";
import { safeReturnTo } from "@/lib/auth/return-to";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Entrar", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

export default async function LoginPage({ searchParams }: Props) {
  const params = await searchParams;
  const returnTo = safeReturnTo(params.voltar);
  if (await hasSession()) redirect(returnTo);

  return (
    <AuthShell
      title="Entrar"
      intro={params.criada ? "Conta criada. Entre com o e-mail e a senha que você escolheu." : undefined}
    >
      <LoginForm returnTo={returnTo} />
      <p className="text-ink-soft">
        Ainda não tem conta?{" "}
        <Link
          href={`/cadastro?voltar=${encodeURIComponent(returnTo)}`}
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Criar conta
        </Link>
      </p>
    </AuthShell>
  );
}
