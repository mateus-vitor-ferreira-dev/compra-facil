import { ChevronRight } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { OrderBadge } from "@/components/account/order-badge";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { EmptyCartIllustration } from "@/components/brand/illustrations";
import { requireAccount } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { orderNumber } from "@/lib/order-status";
import { listMyOrders } from "@/lib/orders-api";
import { logout } from "../auth-actions";

export const metadata: Metadata = { title: "Minha conta", robots: { index: false } };

const dateFormat = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  year: "numeric",
  timeZone: "America/Sao_Paulo",
});

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** A conta do cliente: quem está logado, "Meus pedidos" e sair. */
export default async function AccountPage({ searchParams }: Props) {
  await connection();
  const cursor = (await searchParams).cursor;
  const { account, token } = await requireAccount("/conta");
  const orders = await listMyOrders(token, typeof cursor === "string" ? cursor : undefined);
  const firstName = account.name.split(" ")[0];

  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-8 sm:px-6">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Olá, {firstName}</h1>
          <p className="text-ink-soft" data-testid="account-email">
            {account.name} ({account.email})
          </p>
        </div>
        <div className="flex flex-wrap gap-2">
          {account.roles.includes("admin") && (
            <Link href="/admin" className={buttonVariants({ variant: "primary", size: "sm" })}>
              Abrir o painel
            </Link>
          )}
          <form action={logout}>
            <SubmitButton variant="secondary" size="sm" pendingLabel="Saindo…">
              Sair
            </SubmitButton>
          </form>
        </div>
      </div>

      <section aria-labelledby="pedidos" className="mt-10 max-w-3xl">
        <h2 id="pedidos" className="text-xl font-semibold tracking-tight">
          Meus pedidos
        </h2>
        {!orders.ok ? (
          <StateMessage
            tone="error"
            headingLevel={3}
            title="Os pedidos não carregaram"
            description="O serviço de pedidos não respondeu agora. Tente de novo em instantes."
            action={
              <Link href="/conta" className={buttonVariants({ variant: "secondary" })}>
                Tentar de novo
              </Link>
            }
          />
        ) : orders.data.data.length === 0 ? (
          <StateMessage
            headingLevel={3}
            illustration={<EmptyCartIllustration />}
            title="Nenhum pedido ainda"
            description="Os pedidos que você fizer aparecem aqui, com a situação de cada um."
            action={
              <Link href="/ofertas" className={buttonVariants()}>
                Ver as ofertas
              </Link>
            }
          />
        ) : (
          <>
            <ul className="divide-line border-line mt-4 divide-y border-y">
              {orders.data.data.map((order) => {
                const units = order.items.reduce((n, i) => n + i.quantity, 0);
                return (
                  <li key={order.id}>
                    <Link
                      href={`/conta/pedidos/${order.id}`}
                      className="hover:bg-ink/4 -mx-2 grid grid-cols-[minmax(0,1fr)_auto_auto] items-center gap-x-4 gap-y-1 rounded-md px-2 py-4"
                    >
                      <span className="font-semibold">Pedido {orderNumber(order.id)}</span>
                      <span className="font-condensed row-span-2 text-xl font-extrabold">
                        {formatMoney(order.total)}
                      </span>
                      <span className="flex flex-wrap items-center gap-2 text-sm">
                        <OrderBadge order={order} />
                        <span className="text-ink-soft">
                          {dateFormat.format(new Date(order.created_at))},{" "}
                          {units === 1 ? "1 produto" : `${units} produtos`}
                        </span>
                        {order.status === "shipped" && order.tracking && (
                          // o código para consultar já daqui; o link da transportadora fica no pedido
                          <span className="text-ink-soft">
                            Rastreio <span className="text-ink tabular-nums">{order.tracking.number}</span>
                          </span>
                        )}
                      </span>
                      <ChevronRight
                        aria-hidden="true"
                        className="text-ink-soft col-start-3 row-span-2 row-start-1 size-5"
                      />
                    </Link>
                  </li>
                );
              })}
            </ul>
            {orders.data.page.has_more && orders.data.page.next_cursor && (
              <Link
                href={`/conta?cursor=${encodeURIComponent(orders.data.page.next_cursor)}`}
                className={buttonVariants({ variant: "secondary", className: "mt-6" })}
              >
                Ver pedidos mais antigos
              </Link>
            )}
          </>
        )}
      </section>
    </main>
  );
}
