"use client";

import { Button } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";

export function ToastDemo() {
  const { show } = useToast();
  return (
    <div className="flex flex-wrap gap-3">
      <Button variant="secondary" onClick={() => show("success", "Produto adicionado ao carrinho")}>
        Aviso de sucesso
      </Button>
      <Button variant="secondary" onClick={() => show("error", "Não deu para adicionar: o estoque acabou")}>
        Aviso de erro
      </Button>
    </div>
  );
}
