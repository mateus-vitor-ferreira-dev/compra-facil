"use server";

import { redirect } from "next/navigation";
import { DLT_SERVICES, replayDeadLetters } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

export async function replay(form: FormData): Promise<void> {
  const service = DLT_SERVICES.find((s) => s.key === form.get("service"))?.key;
  if (!service) redirect("/admin/falhas");
  const { token } = await requireAdmin("/admin/falhas");
  const result = await replayDeadLetters(token, service);
  redirect(
    result.ok
      ? `/admin/falhas?reprocessadas=${result.data.replayed}&servico=${service}`
      : `/admin/falhas?erro=${result.error.status}&servico=${service}`,
  );
}
