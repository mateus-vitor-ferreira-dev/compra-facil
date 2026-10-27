import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { GoogleButton } from "@/components/account/google-button";
import { hasSession } from "@/lib/auth/flows";
import { safeReturnTo } from "@/lib/auth/return-to";
import { RegisterForm } from "./register-form";

export const metadata: Metadata = { title: "Criar conta", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

export default async function RegisterPage({ searchParams }: Props) {
  const returnTo = safeReturnTo((await searchParams).voltar);
  if (await hasSession()) redirect(returnTo);

  return (
    <AuthShell title="Criar conta" intro="Para acompanhar os pedidos e comprar sem preencher tudo de novo.">
      <GoogleButton returnTo={returnTo} />
      <RegisterForm returnTo={returnTo} />
      <p className="text-ink-soft">
        Já tem conta?{" "}
        <Link
          href={`/entrar?voltar=${encodeURIComponent(returnTo)}`}
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Entrar
        </Link>
      </p>
    </AuthShell>
  );
}
