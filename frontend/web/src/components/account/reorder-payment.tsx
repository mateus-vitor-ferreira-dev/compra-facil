"use client";

import { refazerPedido } from "@/app/(loja)/conta/pedidos/[id]/actions";
import { SubmitButton } from "@/components/ui/submit-button";

/**
 * Depois do pagamento recusado: os produtos voltam ao carrinho em um clique. No PIX vencido (#343), o cliente paga
 * de novo dentro do prazo; no cartão recusado (#363), tenta outro cartão ou o PIX, sem procurar cada produto.
 */
export function ReorderPayment({ orderId, method }: { orderId: string; method?: "pix" | "card" }) {
  return (
    <form action={refazerPedido} className="space-y-2">
      <input type="hidden" name="order_id" value={orderId} />
      <SubmitButton pendingLabel="Devolvendo ao carrinho…">
        {method === "card" ? "Tentar de novo" : "Fazer o pedido de novo"}
      </SubmitButton>
      <p className="text-ink-soft text-sm">
        {method === "card"
          ? "Os produtos voltam ao carrinho, para você pagar com outro cartão ou com o PIX."
          : "Os produtos voltam ao carrinho, e o PIX novo vale 30 minutos."}
      </p>
    </form>
  );
}
