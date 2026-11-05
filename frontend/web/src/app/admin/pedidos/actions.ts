"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { refundOrderAgain } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

/** Estorna de novo o pedido cujo estorno o provedor não realizou (#396). */
export async function refundAgain(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const failedAt = z.iso.datetime({ offset: true }).parse(form.get("failed_at"));
  const page = `/admin/pedidos/${id}`;
  const { token } = await requireAdmin(page);
  const result = await refundOrderAgain(token, id, failedAt);
  redirect(result.ok ? `${page}?estorno=pedido` : `${page}?estorno=erro&status=${result.error.status}`);
}
