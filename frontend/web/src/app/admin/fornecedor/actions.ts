"use server";

import { redirect } from "next/navigation";
import type { FormState } from "@/app/admin/produtos/actions";
import { deletePricingRule, setPricingRule, syncSupplierCatalog } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { parseReais } from "@/lib/money-input";

const PAGE = "/admin/fornecedor";

/** A regra padrão (sem category_id) ou a de uma categoria. */
export async function savePricingRule(_: FormState, form: FormData): Promise<FormState> {
  const { token } = await requireAdmin(PAGE);
  const values = {
    category_id: String(form.get("category_id") ?? ""),
    margin_percent: String(form.get("margin_percent") ?? "").trim(),
    min_price: String(form.get("min_price") ?? "").trim(),
  };
  const fields: Record<string, string> = {};
  const margin = Number(values.margin_percent);
  if (!/^\d+$/.test(values.margin_percent) || margin > 1000)
    fields.margin_percent = "Use um número inteiro de 0 a 1000.";
  const min = values.min_price === "" ? 0 : parseReais(values.min_price);
  if (min === null) fields.min_price = "Use um valor em reais, como 19,90.";
  if (form.has("category_id") && !values.category_id) fields.category_id = "Escolha a categoria.";
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  const result = await setPricingRule(token, values.category_id || null, {
    margin_percent: margin,
    min_price_cents: min!,
  });
  if (!result.ok) {
    return {
      values,
      fields: result.error.fields
        ? Object.fromEntries(
            Object.entries(result.error.fields).map(([k, v]) => [
              k === "min_price_cents" ? "min_price" : k,
              v,
            ]),
          )
        : undefined,
      error: {
        message: `A regra não foi gravada (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`${PAGE}?regra=${values.category_id || "padrao"}&reprecificados=${result.data.repriced}`);
}

export async function removePricingRule(form: FormData): Promise<void> {
  const { token } = await requireAdmin(PAGE);
  const id = String(form.get("category_id") ?? "");
  const result = await deletePricingRule(token, id);
  redirect(
    result.ok
      ? `${PAGE}?removida=1&reprecificados=${result.data.repriced}`
      : `${PAGE}?erro=${result.error.status}`,
  );
}

export async function syncFeed(): Promise<void> {
  const { token } = await requireAdmin(PAGE);
  const result = await syncSupplierCatalog(token);
  if (!result.ok) redirect(`${PAGE}?feed=erro&status=${result.error.status}`);
  const r = result.data;
  redirect(
    `${PAGE}?feed=ok&novos=${r.created}&alterados=${r.updated}&fora=${r.removed}&iguais=${r.unchanged}`,
  );
}
