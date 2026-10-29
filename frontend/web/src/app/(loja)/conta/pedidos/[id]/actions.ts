"use server";

import { revalidatePath } from "next/cache";
import { redirect } from "next/navigation";
import { z } from "zod";
import { requireAccount } from "@/lib/auth/current";
import { returnOrder } from "@/lib/orders-api";

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
