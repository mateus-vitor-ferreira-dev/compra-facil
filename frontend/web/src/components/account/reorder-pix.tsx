"use client";

import { refazerPedido } from "@/app/(loja)/conta/pedidos/[id]/actions";
import { SubmitButton } from "@/components/ui/submit-button";

/** Depois do PIX vencido: os produtos voltam ao carrinho, e o cliente paga de novo dentro do prazo (#343). */
export function ReorderPix({ orderId }: { orderId: string }) {
  return (
    <form action={refazerPedido} className="space-y-2">
      <input type="hidden" name="order_id" value={orderId} />
      <SubmitButton pendingLabel="Devolvendo ao carrinho…">Fazer o pedido de novo</SubmitButton>
      <p className="text-ink-soft text-sm">Os produtos voltam ao carrinho, e o PIX novo vale 30 minutos.</p>
    </form>
  );
}
