import { ThemeMenu } from "@/components/theme/theme-menu";
import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import type { ReactNode } from "react";
import { AdminNav } from "@/components/admin/admin-nav";
import { LogoMark } from "@/components/brand/logo";
import { SubmitButton } from "@/components/ui/submit-button";
import { requireAdmin } from "@/lib/auth/current";
import { brand } from "@/lib/brand";
import { logout } from "../(loja)/auth-actions";

export const metadata: Metadata = {
  title: { default: "Painel", template: `%s | Painel ${brand.name}` },
  robots: { index: false, follow: false },
};

/**
 * O painel da equipe (#154): só papel `admin`. A coluna escura do menu separa o bastidor da vitrine; o
 * resto é a mesma tinta e papel da loja, mais denso (#382).
 */
export default async function AdminLayout({ children }: { children: ReactNode }) {
  await connection();
  const { account } = await requireAdmin("/admin");

  return (
    // telas grandes: a coluna do menu à esquerda e o conteúdo à direita; celular: a barra em cima
    <div className="lg:grid lg:min-h-dvh lg:grid-cols-[16rem_minmax(0,1fr)]">
      <AdminNav
        brand={
          <Link href="/admin" className="inline-flex items-center gap-2 rounded-md">
            <LogoMark className="text-on-hero [&_.fill-paper]:fill-hero [&_.stroke-paper]:stroke-hero size-7" />
            <span className="font-bold tracking-tight">Painel {brand.name}</span>
          </Link>
        }
        account={
          <div className="space-y-2 text-sm">
            <p className="text-on-hero/75 truncate px-1" title={account.email}>
              {account.email}
            </p>
            <div className="flex flex-wrap items-center gap-1">
              <ThemeMenu className="text-on-hero hover:bg-on-hero/10" />
              <Link
                href="/"
                className="hover:bg-on-hero/10 inline-flex min-h-11 items-center rounded-md px-3 font-medium"
              >
                Ver a loja
              </Link>
              <form action={logout}>
                <SubmitButton
                  variant="ghost"
                  size="sm"
                  pendingLabel="Saindo…"
                  className="text-on-hero hover:bg-on-hero/10"
                >
                  Sair
                </SubmitButton>
              </form>
            </div>
          </div>
        }
      />
      <main id="conteudo" className="mx-auto w-full max-w-7xl px-4 py-6 sm:px-6 lg:px-8 lg:py-8">
        {children}
      </main>
    </div>
  );
}
