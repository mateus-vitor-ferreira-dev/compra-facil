"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import type { FormState } from "@/app/admin/produtos/actions";
import {
  cancelSupplierOrder,
  correctOrderPlacement,
  markOrderDelivered,
  markOrderOutOfStock,
  markOrderPlaced,
  markOrderShipped,
} from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { parseReais } from "@/lib/money-input";

const PAGE = "/admin/pedidos-a-fazer";
const text = (form: FormData, name: string) => String(form.get(name) ?? "").trim();

type PlacementValues = { supplier_order_id: string; paid_products: string; paid_freight: string };

/** O número no fornecedor e os valores pagos digitados, já conferidos; ou os erros de cada campo. */
function readPlacement(form: FormData) {
  const values: PlacementValues = {
    supplier_order_id: text(form, "supplier_order_id"),
    paid_products: text(form, "paid_products"),
    paid_freight: text(form, "paid_freight"),
  };
  const fields: Record<string, string> = {};
  if (!values.supplier_order_id || values.supplier_order_id.length > 100)
    fields.supplier_order_id = "Copie o número do pedido do site do fornecedor.";
  const products = values.paid_products === "" ? undefined : parseReais(values.paid_products);
  if (products === null || products === 0) fields.paid_products = "Use um valor em reais, como 89,90.";
  const freight = values.paid_freight === "" ? undefined : parseReais(values.paid_freight);
  if (freight === null) fields.paid_freight = "Use um valor em reais, como 19,90, ou deixe em branco.";
  return {
    values,
    fields,
    body: {
      supplier_order_id: values.supplier_order_id,
      paid_product_cost_cents: products ?? undefined,
      paid_freight_cost_cents: freight ?? undefined,
    },
  };
}

/** A equipe fez o pedido no fornecedor: o número dele e, se quiser, o que pagou (a margem passa a usar o pago). */
export async function placeOrder(_: FormState, form: FormData): Promise<FormState> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const { values, fields, body } = readPlacement(form);
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  const result = await markOrderPlaced(token, id, body);
  if (!result.ok) {
    return {
      values,
      error: {
        message:
          result.error.status === 409
            ? "Este pedido já saiu da fila: atualize a página."
            : `O pedido não foi registrado (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`${PAGE}?feito=${id}`);
}

/**
 * Corrige o que foi registrado no pedido feito (#480): o número no fornecedor e o que a equipe pagou de fato, como
 * o débito que aparece no extrato do fornecedor. A margem passa a usar os valores novos; o antes fica guardado.
 */
export async function correctPlacement(_: FormState, form: FormData): Promise<FormState> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const { values, fields, body } = readPlacement(form);
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  const result = await correctOrderPlacement(token, id, body);
  if (!result.ok) {
    return {
      values,
      error: {
        message:
          result.error.status === 409
            ? "Este pedido já foi entregue ou cancelado: o custo dele não muda mais."
            : `A correção não foi gravada (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`${PAGE}?corrigido=${id}`);
}

/** O rastreio do fornecedor: o cliente recebe o código. */
export async function shipOrder(_: FormState, form: FormData): Promise<FormState> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const values = { number: text(form, "number"), carrier: text(form, "carrier"), url: text(form, "url") };
  const fields: Record<string, string> = {};
  if (!values.number || values.number.length > 64)
    fields.number = "O código de rastreio, como BR123456789BR.";
  if (values.url && !/^https:\/\/\S+$/.test(values.url)) fields.url = "Um link que comece com https://.";
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  const result = await markOrderShipped(token, id, {
    number: values.number,
    carrier: values.carrier || undefined,
    url: values.url || undefined,
  });
  if (!result.ok) {
    return {
      values,
      fields: result.error.fields,
      error: {
        message: `O rastreio não foi gravado (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`${PAGE}?rastreio=${id}`);
}

/** O fornecedor não tinha o produto: o pedido falha e o cliente é estornado. */
export async function outOfStock(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const result = await markOrderOutOfStock(token, id, text(form, "detail") || undefined);
  redirect(
    result.ok ? `${PAGE}?sem-estoque=${id}` : `${PAGE}?erro=sem-estoque&status=${result.error.status}`,
  );
}

export async function deliverOrder(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const result = await markOrderDelivered(token, id);
  redirect(result.ok ? `${PAGE}?entregue=${id}` : `${PAGE}?erro=entregue&status=${result.error.status}`);
}

/** Cancela e estorna. Depois de feito no fornecedor, só com a confirmação de que a equipe cancelou lá. */
export async function cancelManualOrder(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("order_id"));
  const { token } = await requireAdmin(PAGE);
  const result = await cancelSupplierOrder(token, id, form.get("cancelled_at_supplier") === "on");
  redirect(result.ok ? `${PAGE}?cancelado=${id}` : `${PAGE}?erro=cancelar&status=${result.error.status}`);
}
