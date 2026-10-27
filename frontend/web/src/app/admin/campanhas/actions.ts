"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import {
  campaignProducts,
  createCampaign,
  deleteCampaign,
  setCampaignProducts,
  updateCampaign,
  type AdminError,
} from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { fromLocalInput } from "@/lib/campaign-time";
import { normalize } from "@/lib/storefront";
import type { FormState } from "../produtos/actions";

const text = (form: FormData, name: string) => String(form.get(name) ?? "").trim();
const slugify = (name: string) =>
  normalize(name)
    .replace(/[^a-z0-9]+/g, "-")
    .replace(/^-|-$/g, "")
    .slice(0, 80);

// o catálogo explica a sobreposição no detail; aqui vira a frase do painel
function explain(error: AdminError, what: string): string {
  if (error.status === 409) {
    return error.detail?.includes("campanha")
      ? "Algum produto já está em outra campanha que cruza este período. Um produto fica em uma campanha por vez."
      : "Já existe uma campanha com este endereço.";
  }
  if (error.status === 401 || error.status === 403) return "A sessão de operador acabou. Entre de novo.";
  return `${what} (${error.status}). Tente de novo.`;
}

export async function saveCampaign(_: FormState, form: FormData): Promise<FormState> {
  const id = text(form, "id");
  const { token } = await requireAdmin(id ? `/admin/campanhas/${id}` : "/admin/campanhas/nova");
  const values = {
    name: text(form, "name"),
    slug: text(form, "slug"),
    discount: text(form, "discount"),
    starts: text(form, "starts"),
    ends: text(form, "ends"),
  };
  const fields: Record<string, string> = {};
  const discount = Number(values.discount);
  if (!Number.isInteger(discount) || discount < 1 || discount > 90) fields.discount = "De 1% a 90%.";
  const starts = fromLocalInput(values.starts);
  const ends = fromLocalInput(values.ends);
  if (!starts) fields.starts = "Escolha a data e a hora de início.";
  if (!ends) fields.ends = "Escolha a data e a hora de fim.";
  if (starts && ends && ends <= starts) fields.ends = "O fim vem depois do início.";
  if (Object.keys(fields).length) return { values, fields };

  const body = {
    name: values.name,
    slug: values.slug || slugify(values.name),
    discount_percent: discount,
    starts_at: starts!,
    ends_at: ends!,
  };
  const result = id ? await updateCampaign(token, id, body) : await createCampaign(token, body);
  if (!result.ok) {
    const apiFields = result.error.fields
      ? Object.fromEntries(
          Object.entries(result.error.fields).map(([k, v]) => [
            (
              { discount_percent: "discount", starts_at: "starts", ends_at: "ends" } as Record<string, string>
            )[k] ?? k,
            v,
          ]),
        )
      : undefined;
    return {
      values,
      fields: apiFields,
      error: {
        message: apiFields
          ? "Confira os campos marcados."
          : explain(result.error, "A campanha não foi gravada"),
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`/admin/campanhas/${result.data.id}?salva=1`);
}

export async function removeCampaign(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("id"));
  const { token } = await requireAdmin(`/admin/campanhas/${id}`);
  const result = await deleteCampaign(token, id);
  redirect(result.ok ? "/admin/campanhas?removida=1" : `/admin/campanhas/${id}?erro=remover`);
}

/** Lê a lista atual e grava a nova: o contrato só substitui a lista inteira. */
async function changeProducts(token: string, id: string, change: (ids: string[]) => string[]) {
  const current = await campaignProducts(token, id);
  if (!current.ok) return current;
  return setCampaignProducts(token, id, change(current.data.data.map((p) => p.id)));
}

export async function addCampaignProducts(_: FormState, form: FormData): Promise<FormState> {
  const id = z.uuid().parse(form.get("id"));
  const { token } = await requireAdmin(`/admin/campanhas/${id}`);
  const chosen = form
    .getAll("product_id")
    .map(String)
    .filter((x) => z.uuid().safeParse(x).success);
  if (chosen.length === 0) return { error: { message: "Marque os produtos que entram na campanha." } };
  const result = await changeProducts(token, id, (ids) => [...new Set([...ids, ...chosen])]);
  if (!result.ok) {
    return {
      error: {
        message: explain(result.error, "Os produtos não foram incluídos"),
        traceId: result.error.traceId,
      },
    };
  }
  redirect(`/admin/campanhas/${id}?incluidos=${chosen.length}`);
}

export async function removeCampaignProduct(form: FormData): Promise<void> {
  const id = z.uuid().parse(form.get("id"));
  const productId = z.uuid().parse(form.get("product_id"));
  const { token } = await requireAdmin(`/admin/campanhas/${id}`);
  const result = await changeProducts(token, id, (ids) => ids.filter((x) => x !== productId));
  redirect(`/admin/campanhas/${id}${result.ok ? "?retirado=1" : "?erro=retirar"}`);
}
