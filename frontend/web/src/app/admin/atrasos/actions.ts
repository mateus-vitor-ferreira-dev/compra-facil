"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { cancelSupplierOrder, retrySupplierOrder } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

const PAGE = "/admin/atrasos";

/** Manda tentar agora o pedido que espera a próxima tentativa. */
export async function retryOrder(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const result = await retrySupplierOrder(token, id);
  redirect(result.ok ? `${PAGE}?tentar=${id}` : `${PAGE}?erro=tentar&status=${result.error.status}`);
}

/** Cancela o pedido que não foi despachado: o cliente é estornado. */
export async function cancelOrder(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const result = await cancelSupplierOrder(token, id);
  redirect(result.ok ? `${PAGE}?cancelado=${id}` : `${PAGE}?erro=cancelar&status=${result.error.status}`);
}
