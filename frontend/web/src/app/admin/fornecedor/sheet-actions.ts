"use server";

import { importSupplierSheet, type SheetError } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

export type SheetState =
  | { status: "idle" }
  | { status: "ok"; products: number; created: number; updated: number; removed: number; unchanged: number }
  | { status: "invalid"; errors: SheetError[]; fileName: string }
  | { status: "error"; message: string; traceId?: string };

const MAX_BYTES = 5 * 1024 * 1024;

/** Sobe a planilha do fornecedor manual (#412). Com erro em qualquer linha, nada muda, e a tela lista cada uma. */
export async function uploadSheet(_: SheetState, form: FormData): Promise<SheetState> {
  const { token } = await requireAdmin("/admin/fornecedor");
  const file = form.get("planilha");
  if (!(file instanceof File) || file.size === 0) {
    return { status: "error", message: "Escolha o arquivo CSV da planilha." };
  }
  if (file.size > MAX_BYTES) {
    return {
      status: "error",
      message: "O arquivo passa de 5 MB. Divida a planilha ou tire as colunas extras.",
    };
  }
  const result = await importSupplierSheet(token, await file.arrayBuffer());
  if (result.ok) return { status: "ok", products: result.products, ...result.sync };
  if (result.status === 422 && result.errors.length > 0) {
    return { status: "invalid", errors: result.errors, fileName: file.name };
  }
  return {
    status: "error",
    message:
      result.status === 409
        ? (result.detail ??
          "O fornecedor não está no modo manual, ou outra leitura do catálogo está rodando.")
        : `A planilha não foi enviada (${result.status}). Tente de novo.`,
    traceId: result.traceId,
  };
}
