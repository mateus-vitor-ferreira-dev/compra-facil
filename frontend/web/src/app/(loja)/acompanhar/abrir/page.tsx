import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { AuthShell } from "@/components/account/auth-shell";
import { buttonVariants } from "@/components/ui/button";
import { SubmitButton } from "@/components/ui/submit-button";
import { MASKED_EMAIL } from "@/lib/guest";
import { openGuestLink } from "../actions";

// o token está na URL: nada desta página o manda para outro site. Com no-referrer, o formulário sem JavaScript
// sairia com `Origin: null`, e o Next recusaria a ação
export const metadata: Metadata = {
  title: "Abrir meus pedidos",
  robots: { index: false },
  referrer: "same-origin",
};

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/**
 * A chegada pelo link do e-mail. A sessão só abre no clique: assim um antivírus de e-mail não a abre sozinho,
 * e um link de outra pessoa não entra na conta dela sem o comprador ver. Com outra sessão no navegador, o
 * clique volta aqui (`outra`) e a troca pede confirmação: quem está na conta com senha sai antes de abrir; quem
 * está em outra compra sem conta vê de qual e-mail é a compra do link.
 */
export default async function OpenGuestLinkPage({ searchParams }: Props) {
  const { token, outra, email } = await searchParams;
  if (typeof token !== "string" || !token) redirect("/acompanhar?erro=link");
  if (outra === "conta") {
    return (
      <AuthShell
        title="Este link é de outra compra"
        intro="Você está na sua conta, e este link abre uma compra feita sem conta. Para abri-la, saia da sua conta neste navegador."
      >
        <SwitchForm token={token} label="Sair e abrir o link" />
        <KeepLink label="Continuar na minha conta" />
      </AuthShell>
    );
  }
  if (outra === "convidado" && typeof email === "string" && MASKED_EMAIL.test(email)) {
    return (
      <AuthShell
        title="Abrir outra compra?"
        intro={`Este link abre as compras de ${email}. A compra aberta agora neste navegador fecha; para voltar a ela, use o link do e-mail dela.`}
      >
        <SwitchForm token={token} label={`Abrir as compras de ${email}`} />
        <KeepLink label="Manter a compra aberta" />
      </AuthShell>
    );
  }
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

/** A troca confirmada: a sessão de antes sai e a do link abre. */
function SwitchForm({ token, label }: { token: string; label: string }) {
  return (
    <form action={openGuestLink}>
      <input type="hidden" name="token" value={token} />
      <input type="hidden" name="trocar" value="1" />
      {/* o e-mail mascarado pode não caber numa linha no celular */}
      <SubmitButton className="h-auto min-h-11 w-full py-2 whitespace-normal" pendingLabel="Abrindo…">
        {label}
      </SubmitButton>
    </form>
  );
}

function KeepLink({ label }: { label: string }) {
  return (
    <Link href="/conta" className={buttonVariants({ variant: "secondary", className: "w-full" })}>
      {label}
    </Link>
  );
}
