import { supplierSheetTemplate } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

/** O modelo da planilha do fornecedor manual (#412), vindo do fulfillment: o mesmo cabeçalho que ele confere. */
export async function GET() {
  const { token } = await requireAdmin("/admin/fornecedor");
  const csv = await supplierSheetTemplate(token);
  if (csv === null) return new Response("O modelo não carregou. Tente de novo.", { status: 503 });
  return new Response(csv, {
    headers: {
      "Content-Type": "text/csv; charset=utf-8",
      "Content-Disposition": 'attachment; filename="modelo-fornecedor.csv"',
      "Cache-Control": "no-store",
    },
  });
}
