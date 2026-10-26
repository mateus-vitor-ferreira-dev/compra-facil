import type { ReactNode } from "react";
import { SiteFooter } from "@/components/store/site-footer";
import { SiteHeader } from "@/components/store/site-header";
import { readCart } from "@/lib/cart";
import { itemCount } from "@/lib/cart-rules";
import { categoryTree, getCategories } from "@/lib/catalog-api";

/** As páginas da loja: cabeçalho com busca e categorias, e o rodapé. */
export default async function StoreLayout({ children }: { children: ReactNode }) {
  const [categories, cart] = await Promise.all([getCategories(), readCart()]);
  // sem categorias (catálogo fora do ar) a loja abre do mesmo jeito, só sem o menu
  const tree = categories.ok ? categoryTree(categories.data) : [];
  return (
    <>
      <SiteHeader categories={tree} cartCount={itemCount(cart)} />
      {children}
      <SiteFooter categories={tree} />
    </>
  );
}
