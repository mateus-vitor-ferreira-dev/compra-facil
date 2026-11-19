import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { MFA_COOKIE, MFA_VERIFY_PATH, readPendingMfa } from "@/lib/auth/mfa";
import { SetupFlow } from "./setup-flow";

export const metadata: Metadata = { title: "Ativar a verificação em duas etapas", robots: { index: false } };

/**
 * O primeiro login da equipe (ameaça T8): a senha conferiu, mas o painel só abre com a verificação em duas
 * etapas. A pessoa cadastra o app autenticador (QR code ou chave), confirma com o primeiro código e recebe
 * os códigos de recuperação.
 */
export default async function SetupPage() {
  const pending = readPendingMfa((await cookies()).get(MFA_COOKIE)?.value);
  if (!pending) redirect("/entrar?erro=verificacao-expirada");
  if (pending.status === "mfa_required") redirect(MFA_VERIFY_PATH);

  return (
    <AuthShell
      title="Ative a verificação em duas etapas"
      intro="O painel da loja pede, além da senha, um código do app autenticador no celular. Assim, uma senha vazada sozinha não entra na conta."
    >
      <SetupFlow />
    </AuthShell>
  );
}
