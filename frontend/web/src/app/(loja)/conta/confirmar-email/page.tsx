import type { Metadata } from "next";
import { AuthShell } from "@/components/account/auth-shell";
import { ConfirmForm } from "./confirm-form";

export const metadata: Metadata = { title: "Confirmar e-mail", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** O destino do link do e-mail de confirmação (?conta=&token=). */
export default async function ConfirmEmailPage({ searchParams }: Props) {
  const { conta, token } = await searchParams;
  return (
    <AuthShell title="Confirmar e-mail" intro="Confirme para poder entrar e acompanhar seus pedidos.">
      <ConfirmForm
        accountId={typeof conta === "string" ? conta : ""}
        token={typeof token === "string" ? token : ""}
      />
    </AuthShell>
  );
}
