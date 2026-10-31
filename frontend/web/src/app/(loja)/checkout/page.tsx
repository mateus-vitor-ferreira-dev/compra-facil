import type { Metadata } from "next";
import Link from "next/link";
import { redirect } from "next/navigation";
import { connection } from "next/server";
import { ProductImage } from "@/components/commerce/product-image";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { accessToken, currentAccount } from "@/lib/auth/current";
import { readCart } from "@/lib/cart";
import { loadCartLines } from "@/lib/cart-lines";
import { summarize } from "@/lib/cart-rules";
import { EMPTY_DELIVERY, fromOrder } from "@/lib/delivery";
import { env } from "@/lib/env";
import { formatMoney } from "@/lib/format";
import { listMyOrders } from "@/lib/orders-api";
import { quoteShipping } from "@/lib/shipping-api";
import { CheckoutForm } from "./checkout-form";

export const metadata: Metadata = { title: "Pagamento", robots: { index: false } };

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/**
 * A revisão antes do pedido: os produtos com o preço que vai ser cobrado, a conta que recebe o recibo,
 * como pagar (PIX ou cartão, #220) e o botão que inicia a compra. Só chega aqui um carrinho sem avisos; o que mudou volta para o carrinho,
 * que mostra o quê.
 */
export default async function CheckoutPage() {
  await connection();
  // sem sessão, a compra segue sem conta: o e-mail vem no formulário
  const token = await accessToken();
  const account = token ? await currentAccount() : null;
  const cart = await readCart();
  if (cart.items.length === 0) redirect("/carrinho");

  const { lines, unreachable } = await loadCartLines(cart);
  const summary = summarize(lines);
  if (!summary.canCheckout || unreachable) redirect("/carrinho");
  // a entrega do último pedido: quem compra de novo só confere (sem ela, ou com o orders fora, começa vazia)
  const previous = account && token ? await listMyOrders(token) : undefined;
  const remembered = previous?.ok
    ? previous.data.data.map(fromOrder).find((d) => d !== undefined)
    : undefined;
  // o frete cotado já com o CEP de antes: sem JavaScript, o cliente vê e escolhe do mesmo jeito
  const items = lines.flatMap((l) => (l.product ? [{ sku: l.product.sku, quantity: l.qty }] : []));
  const zip = remembered?.zip_code.replace(/\D/g, "");
  const quote = zip ? await quoteShipping(zip, items) : undefined;

  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
      <Breadcrumbs items={[{ href: "/carrinho", label: "Carrinho" }, { label: "Pagamento" }]} />
      <h1 className="mt-4 text-2xl font-semibold tracking-tight sm:text-3xl">Revise e faça o pedido</h1>

      <CheckoutForm
        idempotencyKey={crypto.randomUUID()}
        itemsTotalCents={summary.total}
        items={items}
        initialQuote={
          quote?.ok && quote.options.length ? { zip: quote.zipCode, options: quote.options } : undefined
        }
        provider={{ url: env.PAYMENT_PROVIDER_URL, publicKey: env.PAYMENT_PUBLIC_KEY }}
        initialDelivery={remembered ?? { ...EMPTY_DELIVERY, name: account?.name ?? "" }}
        remembered={remembered !== undefined}
        guest={!account}
        review={
          <>
            <section aria-labelledby="produtos">
              <div className="flex items-baseline justify-between gap-4">
                <h2 id="produtos" className="text-xl font-semibold tracking-tight">
                  Produtos
                </h2>
                <Link
                  href="/carrinho"
                  className="inline-flex min-h-11 items-center text-sm font-semibold underline-offset-4 hover:underline sm:min-h-8"
                >
                  Alterar o carrinho
                </Link>
              </div>
              <ul className="divide-line border-line mt-3 divide-y border-y">
                {lines.map(({ id, qty, product }) =>
                  product ? (
                    <li key={id} className="grid grid-cols-[4rem_minmax(0,1fr)_auto] items-center gap-4 py-4">
                      <ProductImage
                        image={product.image}
                        alt=""
                        categorySlug={product.category.slug}
                        sizes="64px"
                      />
                      <div className="min-w-0">
                        <p className="leading-snug font-medium">{product.name}</p>
                        <p className="text-ink-soft text-sm">
                          {qty} × {brl(product.effective_price.amount_cents)}
                        </p>
                      </div>
                      <p className="font-condensed text-lg font-extrabold">
                        <span className="sr-only">Subtotal: </span>
                        {brl(product.effective_price.amount_cents * qty)}
                      </p>
                    </li>
                  ) : null,
                )}
              </ul>
            </section>

            {account && (
              <section aria-labelledby="conta" className="space-y-1">
                <h2 id="conta" className="text-xl font-semibold tracking-tight">
                  Conta
                </h2>
                <p>{account.name}</p>
                <p className="text-ink-soft">A confirmação e o recibo vão para {account.email}.</p>
              </section>
            )}
          </>
        }
        summary={
          <>
            <h2 id="resumo" className="text-xl font-semibold tracking-tight">
              Resumo
            </h2>
            <dl className="mt-4 space-y-2">
              <div className="flex justify-between gap-4">
                <dt className="text-ink-soft">
                  {summary.units === 1 ? "1 produto" : `${summary.units} produtos`}
                </dt>
                <dd className="tabular-nums">{brl(summary.list)}</dd>
              </div>
              {summary.savings > 0 && (
                <div className="text-success flex justify-between gap-4 font-semibold">
                  <dt>Descontos de campanha</dt>
                  <dd className="tabular-nums">−{brl(summary.savings)}</dd>
                </div>
              )}
            </dl>
          </>
        }
      />
    </main>
  );
}
