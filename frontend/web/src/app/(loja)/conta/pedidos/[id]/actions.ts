"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { requireAccount } from "@/lib/auth/current";
import { readCart, writeCart } from "@/lib/cart";
import { addItem } from "@/lib/cart-rules";
import { getAvailability } from "@/lib/inventory-api";
import { getOrder, returnOrder } from "@/lib/orders-api";

export type ReturnState = { status: "idle" } | { status: "error"; message: string };

/**
 * O cliente desiste da compra entregue (#223). Deu certo, a página recarrega já com o pedido devolvido e
 * a confirmação no topo (a seção da desistência some, então o aviso não pode ficar nela).
 */
export async function desistir(_: ReturnState, form: FormData): Promise<ReturnState> {
  const id = z.uuid().safeParse(form.get("order_id"));
  if (!id.success) return { status: "error", message: "Pedido inválido." };
  const { token } = await requireAccount(`/conta/pedidos/${id.data}`);
  const result = await returnOrder(token, id.data);
  if (!result.ok) {
    return {
      status: "error",
      message:
        result.error.status === 409
          ? "O prazo de 7 dias para desistir deste pedido acabou."
          : "A desistência não foi registrada: o serviço de pedidos não respondeu. Tente de novo.",
    };
  }
  revalidatePath(`/conta/pedidos/${id.data}`);
  redirect(`/conta/pedidos/${id.data}?desistencia=1`);
}

/**
 * O pagamento foi recusado (o PIX venceu, #343, ou o cartão não passou, #363) e o pedido foi cancelado: os
 * produtos voltam ao carrinho, para o cliente pagar de novo sem refazer a busca. O preço visto é o do pedido: se mudou desde então, o carrinho avisa, como sempre.
 * O que esgotou fica de fora, e o carrinho conta quantos.
 */
export async function refazerPedido(form: FormData): Promise<void> {
  const id = z.uuid().safeParse(form.get("order_id"));
  if (!id.success) redirect("/conta");
  const { token } = await requireAccount(`/conta/pedidos/${id.data}`);
  const order = await getOrder(token, id.data);
  // só o pagamento recusado (PIX vencido, #343, ou cartão, #363): os outros cancelamentos têm outro caminho
  if (!order.ok || order.data.status !== "cancelled" || order.data.cancellation_reason !== "payment_failed") {
    redirect(`/conta/pedidos/${id.data}`);
  }
  const items = order.data.items;
  const stock = await getAvailability(items.map((i) => i.product_id));
  let cart = await readCart();
  let back = 0;
  for (const item of items) {
    const result = addItem(
      cart,
      item.product_id,
      item.quantity,
      item.unit_price.amount_cents,
      stock.ok ? stock.data[item.product_id] : undefined,
    );
    if (result.ok) {
      cart = result.cart;
      back++;
    }
  }
  await writeCart(cart);
  redirect(`/carrinho?refeito=${back}&fora=${items.length - back}`);
}
