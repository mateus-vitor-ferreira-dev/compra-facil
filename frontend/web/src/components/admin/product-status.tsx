import { Badge } from "@/components/ui/badge";
import type { AdminProduct } from "@/lib/admin-api";

type Status = AdminProduct["status"];

export const STATUS_LABEL: Record<Status, string> = {
  active: "À venda",
  draft: "Rascunho",
  inactive: "Fora de venda",
};

/** A situação do produto em palavras da equipe; só "à venda" aparece na vitrine. */
export function ProductStatus({ status }: { status: Status }) {
  return (
    <Badge variant={status === "active" ? "success" : status === "draft" ? "outline" : "neutral"}>
      {STATUS_LABEL[status]}
    </Badge>
  );
}
