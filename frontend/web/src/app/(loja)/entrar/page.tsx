import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { GoogleButton } from "@/components/account/google-button";
import { hasSession } from "@/lib/auth/flows";
import { safeReturnTo } from "@/lib/auth/return-to";
import { LoginForm } from "./login-form";

export const metadata: Metadata = { title: "Entrar", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

// por que o login com o Google não terminou (?erro= do callback)
const GOOGLE_ERRORS: Record<string, string> = {
  "google-cancelado": "O login com o Google foi cancelado. Tente de novo ou entre com e-mail e senha.",
  "google-expirado": "O login com o Google expirou. Comece de novo.",
  "muitas-tentativas": "Muitas tentativas em pouco tempo. Espere um pouco e tente de novo.",
  google: "O Google não confirmou o login. Tente de novo ou entre com e-mail e senha.",
};

export default async function LoginPage({ searchParams }: Props) {
  const params = await searchParams;
  const returnTo = safeReturnTo(params.voltar);
  if (await hasSession()) redirect(returnTo);

  return (
    <AuthShell
      title="Entrar"
      intro={params.criada ? "Conta criada. Entre com o e-mail e a senha que você escolheu." : undefined}
    >
      {typeof params.erro === "string" && GOOGLE_ERRORS[params.erro] && (
        <p role="alert" className="border-danger bg-surface rounded-md border-l-4 px-4 py-3">
          {GOOGLE_ERRORS[params.erro]}
        </p>
      )}
      <GoogleButton returnTo={returnTo} />
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
      <p className="text-ink-soft">
        Comprou sem conta?{" "}
        <Link
          href="/acompanhar"
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Receber o link dos pedidos
        </Link>
      </p>
    </AuthShell>
  );
}
