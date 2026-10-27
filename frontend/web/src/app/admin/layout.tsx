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
 * O painel da equipe (#154): só papel `admin`. A barra escura separa o bastidor da vitrine; o resto
 * é a mesma tinta e papel da loja, mais denso.
 */
export default async function AdminLayout({ children }: { children: ReactNode }) {
  await connection();
  const { account } = await requireAdmin("/admin");

  return (
    <>
      <header className="bg-hero text-on-hero">
        <div className="mx-auto max-w-7xl px-4 sm:px-6">
          <div className="flex min-h-14 flex-wrap items-center justify-between gap-x-4 gap-y-2 py-2">
            <Link href="/admin/produtos" className="inline-flex items-center gap-2 rounded-md">
              <LogoMark className="text-on-hero [&_.fill-paper]:fill-hero [&_.stroke-paper]:stroke-hero size-7" />
              <span className="font-bold tracking-tight">Painel {brand.name}</span>
            </Link>
            <div className="flex items-center gap-2 text-sm">
              <span className="text-on-hero/75 hidden sm:inline">{account.email}</span>
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
          <AdminNav />
        </div>
      </header>
      <main id="conteudo" className="mx-auto max-w-7xl px-4 py-6 sm:px-6">
        {children}
      </main>
    </>
  );
}
