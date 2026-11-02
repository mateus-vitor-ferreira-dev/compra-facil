import { ExternalLink } from "lucide-react";
import Link from "next/link";
import { trackingHref } from "@/lib/order-status";
import type { Order } from "@/lib/orders-api";

/**
 * O rastreio do envio (#224): o código, a transportadora e o link dela, para o cliente acompanhar a
 * entrega sem falar com a loja. A caminho, avisa que a primeira movimentação pode demorar; entregue, leva ao que
 * fazer se o pacote não chegou (#343).
 */
export function OrderTracking({
  tracking,
  status,
}: {
  tracking: NonNullable<Order["tracking"]>;
  status: Order["status"];
}) {
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
            className="inline-flex min-h-11 items-center gap-1.5 font-semibold underline underline-offset-4 pointer-fine:min-h-8"
          >
            Acompanhar a entrega
            <ExternalLink aria-hidden="true" className="size-4" />
            <span className="sr-only"> (abre em outra aba)</span>
          </a>
        </p>
      )}
      {status === "shipped" && (
        <p className="text-ink-soft pt-2 text-sm">
          O site da transportadora pode levar de 1 a 2 dias úteis para mostrar a primeira movimentação.
        </p>
      )}
      {status === "delivered" && (
        <p className="text-ink-soft pt-2 text-sm">
          Consta como entregue, mas não chegou?{" "}
          <Link
            href="/politicas/entrega#nao-recebi"
            className="text-ink inline-flex min-h-11 items-center font-semibold underline underline-offset-4 pointer-fine:min-h-6"
          >
            Veja o que fazer
          </Link>
        </p>
      )}
    </section>
  );
}
