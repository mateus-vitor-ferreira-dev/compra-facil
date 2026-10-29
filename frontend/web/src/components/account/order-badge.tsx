import { CircleCheck, CircleX, Loader, Package, Truck } from "lucide-react";
import { Badge } from "@/components/ui/badge";
import { orderStatus } from "@/lib/order-status";
import type { Order } from "@/lib/orders-api";

/** O selo da situação: ícone e texto, nunca só a cor. */
const icons = {
  pending: Loader,
  confirmed: Package,
  shipped: Truck,
  delivered: CircleCheck,
  cancelled: CircleX,
} satisfies Record<Order["status"], unknown>;

export function OrderBadge({
  order,
}: {
  order: Pick<Order, "status" | "cancellation_reason" | "sent_to_supplier_at" | "refunded_at">;
}) {
  const s = orderStatus(order);
  const Icon = icons[order.status];
  return (
    <Badge variant={s.tone} className="text-sm">
      <Icon aria-hidden="true" className="size-3.5" />
      {s.label}
    </Badge>
  );
}
