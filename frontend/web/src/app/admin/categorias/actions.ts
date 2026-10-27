"use server";

import { redirect } from "next/navigation";
import { createCategory } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { normalize } from "@/lib/storefront";
import type { FormState } from "../produtos/actions";

/** "Fones de ouvido" → "fones-de-ouvido": o slug sugerido quando a equipe não escreve um. */
const slugify = (name: string) =>
  normalize(name)
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);

export async function saveCategory(_: FormState, form: FormData): Promise<FormState> {
  const { token } = await requireAdmin("/admin/categorias");
  const values = {
    name: String(form.get("name") ?? "").trim(),
    slug: String(form.get("slug") ?? "").trim(),
    parent_id: String(form.get("parent_id") ?? ""),
  };
  const result = await createCategory(token, {
    name: values.name,
    slug: values.slug || slugify(values.name),
    parent_id: values.parent_id || null,
  });
  if (!result.ok) {
    if (result.error.fields)
      return { values, fields: result.error.fields, error: { message: "Confira os campos marcados." } };
    return {
      values,
      fields:
        result.error.status === 409 ? { slug: "Já existe uma categoria com este endereço." } : undefined,
      error: {
        message:
          result.error.status === 409
            ? "Já existe uma categoria com este endereço."
            : `A categoria não foi criada (${result.error.status}). Tente de novo.`,
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`/admin/categorias?criada=${encodeURIComponent(result.data.name)}`);
}
