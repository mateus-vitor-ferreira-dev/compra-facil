import type { Metadata } from "next";
import Link from "next/link";
import { AuthShell } from "@/components/account/auth-shell";
import { GuestLinkForm } from "./guest-link-form";

export const metadata: Metadata = { title: "Acompanhar compra", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** Para quem comprou sem conta e perdeu o link: é por ele que se vê o pedido e se desiste da compra. */
export default async function GuestLinkPage({ searchParams }: Props) {
  const invalid = (await searchParams).erro === "link";
  return (
    <AuthShell
      title="Acompanhar compra"
      intro="Comprou sem criar conta? Enviamos um link para abrir os seus pedidos, sem senha."
    >
      {invalid && (
        <p role="alert" className="border-danger bg-surface rounded-md border-l-4 px-4 py-3">
          Esse link não vale mais. Peça outro abaixo.
        </p>
      )}
      <GuestLinkForm />
      <p className="text-ink-soft">
        Tem conta com senha?{" "}
        <Link
          href="/entrar"
          className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4"
        >
          Entrar
        </Link>
      </p>
    </AuthShell>
  );
}
