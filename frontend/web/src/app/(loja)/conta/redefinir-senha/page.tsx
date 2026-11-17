import type { Metadata } from "next";
import { AuthShell } from "@/components/account/auth-shell";
import { ResetForm } from "./reset-form";

export const metadata: Metadata = { title: "Criar senha nova", robots: { index: false } };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** O destino do link do e-mail "Redefinir sua senha" (?conta=&token=). */
export default async function ResetPasswordPage({ searchParams }: Props) {
  const { conta, token } = await searchParams;
  return (
    <AuthShell title="Criar senha nova" intro="Depois de trocar, a conta sai dos outros aparelhos.">
      <ResetForm
        accountId={typeof conta === "string" ? conta : ""}
        token={typeof token === "string" ? token : ""}
      />
    </AuthShell>
  );
}
