import type { ReactNode } from "react";
import { StoreChrome } from "@/components/store/store-chrome";

/** As páginas da loja: cabeçalho com busca e categorias, e o rodapé. */
export default function StoreLayout({ children }: { children: ReactNode }) {
  return <StoreChrome>{children}</StoreChrome>;
}
