import { CircleCheck, CircleX, Loader } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { orderStatus } from "@/lib/order-status";
import type { Order } from "@/lib/orders-api";

/** O selo da situação: ícone e texto, nunca só a cor. */
export function OrderBadge({ order }: { order: Pick<Order, "status" | "cancellation_reason"> }) {
  const s = orderStatus(order.status, order.cancellation_reason);
  const Icon = s.tone === "success" ? CircleCheck : s.tone === "danger" ? CircleX : Loader;
  return (
    <Badge variant={s.tone} className="text-sm">
      <Icon aria-hidden="true" className="size-3.5" />
      {s.label}
    </Badge>
  );
}
