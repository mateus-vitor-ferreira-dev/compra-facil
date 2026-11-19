import type { Metadata } from "next";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { RecoveryCodes } from "@/components/account/recovery-codes";
import { SubmitButton } from "@/components/ui/submit-button";
import { RECOVERY_CODES_COOKIE, readRecoveryCodes } from "@/lib/auth/recovery-codes";
import { continueAfterCodes } from "./actions";

export const metadata: Metadata = { title: "Códigos de recuperação", robots: { index: false } };

/** Logo depois de ativar a verificação em duas etapas: os códigos de recuperação, uma única vez. */
export default async function RecoveryCodesPage() {
  const saved = readRecoveryCodes((await cookies()).get(RECOVERY_CODES_COOKIE)?.value);
  if (!saved) redirect("/admin");

  return (
    <AuthShell
      title="Verificação em duas etapas ativa"
      intro="Daqui em diante, o login pede a senha e o código do app autenticador."
    >
      <RecoveryCodes codes={saved.codes} />
      <form action={continueAfterCodes}>
        <SubmitButton className="w-full" pendingLabel="Abrindo…">
          Guardei os códigos, continuar
        </SubmitButton>
      </form>
    </AuthShell>
  );
}
