"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import {
  adjustStock,
  createProduct,
  deleteProduct,
  updateProduct,
  type AdminError,
  type ProductInput,
} from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { parseReais } from "@/lib/money-input";

export type FormState = {
  /** o erro geral, com o código para o suporte quando há */
  error?: { message: string; traceId?: string };
  /** o erro de cada campo, junto do campo */
  fields?: Record<string, string>;
  /** o que foi digitado, para não perder num erro */
  values?: Record<string, string>;
};

const text = (form: FormData, name: string) => String(form.get(name) ?? "").trim();

// os nomes dos campos da API, na língua do formulário
const FIELD = { price_cents: "price", category_id: "category_id", image_url: "image_url" } as Record<
  string,
  string
>;

function failure(error: AdminError, values: Record<string, string>): FormState {
  if (error.fields) {
    return {
      values,
      fields: Object.fromEntries(Object.entries(error.fields).map(([k, v]) => [FIELD[k] ?? k, v])),
      error: { message: "Confira os campos marcados." },
    };
  }
  const message =
    error.status === 409
      ? "Já existe um produto com este SKU."
      : error.status === 412
        ? "Outra pessoa alterou este produto enquanto você editava. Recarregue a página para ver a versão nova e refazer a sua mudança."
        : error.status === 401 || error.status === 403
          ? "A sessão de operador acabou. Entre de novo."
          : `O catálogo não gravou (${error.status}${error.detail ? `: ${error.detail}` : ""}). Tente de novo.`;
  return {
    values,
    error: { message, traceId: error.traceId },
    fields: error.status === 409 ? { sku: "Este SKU já está em uso." } : undefined,
  };
}

export async function saveProduct(_: FormState, form: FormData): Promise<FormState> {
  const id = text(form, "id");
  const { token } = await requireAdmin(id ? `/admin/produtos/${id}` : "/admin/produtos/novo");
  const values = {
    name: text(form, "name"),
    sku: text(form, "sku"),
    brand: text(form, "brand"),
    description: text(form, "description"),
    category_id: text(form, "category_id"),
    image_url: text(form, "image_url"),
    price: text(form, "price"),
    status: text(form, "status"),
  };
  const price = parseReais(values.price);
  if (price === null || price < 1) {
    return { values, fields: { price: "Escreva o preço em reais, como 1.299,90." } };
  }
  const status = z.enum(["draft", "active", "inactive"]).safeParse(values.status);
  const body: ProductInput = {
    name: values.name,
    sku: values.sku.toUpperCase(),
    brand: values.brand || null,
    description: values.description,
    category_id: values.category_id,
    image_url: values.image_url || null,
    price_cents: price,
    status: status.success ? status.data : "draft",
  };

  const result = id
    ? await updateProduct(token, id, text(form, "etag"), body)
    : await createProduct(token, body);
  if (!result.ok) return failure(result.error, values);
  redirect(`/admin/produtos/${result.data.id}?salvo=1`);
}

export async function removeProduct(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("id"));
  const { token } = await requireAdmin(`/admin/produtos/${id}`);
  const result = await deleteProduct(token, id);
  redirect(result.ok ? "/admin/produtos?removido=1" : `/admin/produtos/${id}?erro=remover`);
}

export type StockState = FormState;

const REASONS = ["restock", "inventory_count", "damage", "other"] as const;

export async function saveStockAdjustment(_: StockState, form: FormData): Promise<StockState> {
  const id = z.uuid().parse(form.get("id"));
  const { token } = await requireAdmin(`/admin/produtos/${id}`);
  const values = { delta: text(form, "delta"), reason: text(form, "reason"), note: text(form, "note") };
  const delta = Number(values.delta.replace("+", ""));
  const reason = REASONS.find((r) => r === values.reason);
  const fields: Record<string, string> = {};
  if (!Number.isInteger(delta) || delta === 0 || Math.abs(delta) > 100_000) {
    fields.delta = "Quantas unidades entram (ex.: 20) ou saem (ex.: -3); diferente de zero.";
  }
  if (!reason) fields.reason = "Escolha o motivo.";
  if (Object.keys(fields).length) return { values, fields };

  const key = z.uuid().safeParse(form.get("idempotency_key"));
  const result = await adjustStock(token, id, key.success ? key.data : crypto.randomUUID(), {
    delta,
    reason: reason!,
    note: values.note || undefined,
  });
  if (!result.ok) {
    return {
      values,
      error: {
        message:
          result.error.status === 409
            ? "A baixa deixaria o estoque abaixo do que já está reservado para pedidos."
            : `O estoque não foi ajustado (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`/admin/produtos/${id}?estoque=${result.data.on_hand}`);
}
