"use server";

import { redirect } from "next/navigation";
import type { FormState } from "@/app/admin/produtos/actions";
import { requireAdmin } from "@/lib/auth/current";
import { fromLocalInput } from "@/lib/campaign-time";
import { saveSchedule } from "@/lib/maintenance";

const PAGE = "/admin/manutencao";
const text = (form: FormData, name: string) => String(form.get(name) ?? "").trim();
const DAY = 24 * 60 * 60 * 1000;
// o aviso fica no KV até o fim da janela: mais longe que isso, a faixa anunciaria algo que ninguém lembra
const MAX_AHEAD_DAYS = 90;

/** Agenda a janela de manutenção (#370): a loja mostra a faixa antes e durante ela. Substitui a anterior. */
export async function scheduleMaintenance(_: FormState, form: FormData): Promise<FormState> {
  await requireAdmin(PAGE);
  const values = { starts: text(form, "starts"), ends: text(form, "ends"), message: text(form, "message") };
  const starts = fromLocalInput(values.starts);
  const ends = fromLocalInput(values.ends);
  const fields: Record<string, string> = {};
  if (!starts) fields.starts = "Escolha a data e a hora do início.";
  if (!ends) fields.ends = "Escolha a data e a hora do fim.";
  if (starts && ends) {
    if (Date.parse(ends) <= Date.parse(starts)) fields.ends = "O fim vem depois do início.";
    else if (Date.parse(ends) <= Date.now()) fields.ends = "O fim já passou.";
    else if (Date.parse(ends) - Date.parse(starts) > DAY) fields.ends = "A janela tem no máximo 24 horas.";
    if (Date.parse(starts) - Date.now() > MAX_AHEAD_DAYS * DAY)
      fields.starts = `Agende com até ${MAX_AHEAD_DAYS} dias de antecedência.`;
  }
  if (values.message.length > 200) fields.message = "Use até 200 caracteres.";
  if (Object.keys(fields).length > 0)
    return { values, fields, error: { message: "Confira os campos marcados." } };

  try {
    await saveSchedule({ starts_at: starts!, ends_at: ends!, message: values.message || undefined });
  } catch {
    return {
      values,
      error: {
        message: "A manutenção não foi agendada: o armazenamento do aviso não respondeu. Tente de novo.",
      },
    };
  }
  redirect(`${PAGE}?agendada=1`);
}

/** Cancela a janela: a faixa some da loja em até meio minuto. */
export async function cancelMaintenance(): Promise<void> {
  await requireAdmin(PAGE);
  await saveSchedule(undefined);
  redirect(`${PAGE}?cancelada=1`);
}
