import { ExternalLink } from "lucide-react";
import { trackingHref } from "@/lib/order-status";
import type { Order } from "@/lib/orders-api";

/**
 * O rastreio do envio (#224): o código, a transportadora e o link dela, para o cliente acompanhar a
 * entrega sem falar com a loja.
 */
export function OrderTracking({ tracking }: { tracking: NonNullable<Order["tracking"]> }) {
  const href = trackingHref(tracking.url);
  return (
    <section aria-labelledby="rastreio" className="border-line rounded-lg border p-4 sm:p-5">
      <h2 id="rastreio" className="font-semibold">
        Rastreio
        {tracking.provider && <span className="text-ink-soft font-normal"> · {tracking.provider}</span>}
      </h2>
      <p className="font-condensed mt-1 text-2xl font-extrabold tracking-wide break-all tabular-nums select-all">
        {tracking.number}
      </p>
      {href && (
        <p className="pt-1">
          <a
            href={href}
            target="_blank"
            rel="noreferrer"
            className="inline-flex min-h-11 items-center gap-1.5 font-semibold underline underline-offset-4 sm:min-h-8"
          >
            Acompanhar a entrega
            <ExternalLink aria-hidden="true" className="size-4" />
            <span className="sr-only"> (abre em outra aba)</span>
          </a>
        </p>
      )}
    </section>
  );
}
