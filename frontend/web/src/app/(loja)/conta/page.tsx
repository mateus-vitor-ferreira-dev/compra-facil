import type { Metadata } from "next";
import { currentAccount } from "@/lib/auth/current";

export const metadata: Metadata = { title: "Minha conta", robots: { index: false } };

/** Provisória (#148): mostra a conta da sessão do BFF. A área do cliente de verdade vem na #152. */
export default async function Account() {
  const account = await currentAccount();
  return (
    <main id="conteudo" className="mx-auto max-w-3xl px-6 py-12">
      <h1 className="text-2xl font-semibold">Minha conta</h1>
      {account ? (
        <p className="mt-4" data-testid="account-email">
          {account.name} ({account.email})
        </p>
      ) : (
        <p className="mt-4">Você não está logado.</p>
      )}
    </main>
  );
}
