import type { Metadata } from "next";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { SubmitButton } from "@/components/ui/submit-button";
import { openGuestLink } from "../actions";

// o token está na URL: nada desta página o manda para outro site
export const metadata: Metadata = {
  title: "Abrir meus pedidos",
  robots: { index: false },
  referrer: "no-referrer",
};

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/**
 * A chegada pelo link do e-mail. A sessão só abre no clique: assim um antivírus de e-mail não a abre sozinho,
 * e um link de outra pessoa não entra na conta dela sem o comprador ver.
 */
export default async function OpenGuestLinkPage({ searchParams }: Props) {
  const { token } = await searchParams;
  if (typeof token !== "string" || !token) redirect("/acompanhar?erro=link");
  return (
    <AuthShell
      title="Abrir meus pedidos"
      intro="Este link é da sua compra. Não o encaminhe a outras pessoas."
    >
      <form action={openGuestLink}>
        <input type="hidden" name="token" value={token} />
        <SubmitButton className="w-full" pendingLabel="Abrindo…">
          Abrir meus pedidos
        </SubmitButton>
      </form>
    </AuthShell>
  );
}
