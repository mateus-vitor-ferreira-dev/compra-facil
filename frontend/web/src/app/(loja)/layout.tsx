import type { ReactNode } from "react";
import { SiteFooter } from "@/components/store/site-footer";
import { SiteHeader } from "@/components/store/site-header";
import { hasSession } from "@/lib/auth/flows";
import { readCart } from "@/lib/cart";
import { itemCount } from "@/lib/cart-rules";
import { categoryTree, getCategories } from "@/lib/catalog-api";
import { env } from "@/lib/env";

/** As páginas da loja: cabeçalho com busca e categorias, e o rodapé. */
export default async function StoreLayout({ children }: { children: ReactNode }) {
  const [categories, cart, signedIn] = await Promise.all([getCategories(), readCart(), hasSession()]);
  // sem categorias (catálogo fora do ar) a loja abre do mesmo jeito, só sem o menu
  const tree = categories.ok ? categoryTree(categories.data) : [];
  return (
    <>
      {env.DEMO_MODE && (
        <p className="bg-hero text-on-hero px-4 py-2 text-center text-sm font-medium">
          Loja de demonstração: nada é cobrado nem entregue. Não use dados reais de cartão.
        </p>
      )}
      <SiteHeader categories={tree} cartCount={itemCount(cart)} signedIn={signedIn} />
      {children}
      <SiteFooter categories={tree} />
    </>
  );
}
