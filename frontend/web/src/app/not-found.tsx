import type { Metadata } from "next";
import { NotFoundContent } from "@/components/store/not-found-content";
import { StoreChrome } from "@/components/store/store-chrome";

// o modelo do layout raiz acrescenta "| CompraFácil"
export const metadata: Metadata = { title: "Página não encontrada" };

/**
 * O endereço que não existe em lugar nenhum (nenhuma rota casa): a 404 da loja, com o cabeçalho e o rodapé,
 * no lugar da página padrão do Next, que vinha em inglês e sem saída.
 */
export default function NotFound() {
  return (
    <StoreChrome>
      <NotFoundContent />
    </StoreChrome>
  );
}
