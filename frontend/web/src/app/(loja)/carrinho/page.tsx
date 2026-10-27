import { Minus, Plus, Trash2 } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { connection } from "next/server";
import { EmptyCartIllustration } from "@/components/brand/illustrations";
import { ProductImage } from "@/components/commerce/product-image";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { readCart, readUndo } from "@/lib/cart";
import { loadCartLines, type CartLine } from "@/lib/cart-lines";
import { MAX_QUANTITY, lineIssue, summarize, type Line } from "@/lib/cart-rules";
import { getProduct } from "@/lib/catalog-api";
import { formatMoney } from "@/lib/format";
import { cn } from "@/lib/utils";
import { acceptCartPrices, removeFromCart, undoRemove, updateQuantity } from "./actions";

export const metadata: Metadata = { title: "Carrinho", robots: { index: false } };

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** O carrinho: cada linha com o preço de agora, o estoque e o que precisa de atenção antes do pedido. */
export default async function CartPage({ searchParams }: Props) {
  await connection();
  // ?conferir=1: o pedido foi recusado porque um preço ou um produto mudou no meio do caminho
  const recheck = (await searchParams).conferir === "1";
  const [cart, removed] = await Promise.all([readCart(), readUndo()]);
  const removedProduct =
    removed && !cart.items.some((i) => i.id === removed.id) ? await getProduct(removed.id) : undefined;
  const undo = removedProduct?.ok && (
    <div
      role="status"
      className="bg-surface border-line mt-4 flex flex-wrap items-center justify-between gap-3 rounded-md border p-4"
    >
      <p>
        <span className="font-semibold">{removedProduct.data.name}</span> saiu do carrinho.
      </p>
      <form action={undoRemove}>
        <SubmitButton variant="secondary" size="sm" pendingLabel="Devolvendo…">
          Desfazer
        </SubmitButton>
      </form>
    </div>
  );

  if (cart.items.length === 0) {
    return (
      <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
        <h1 className="sr-only">Carrinho</h1>
        {undo}
        <StateMessage
          illustration={<EmptyCartIllustration />}
          title="Seu carrinho está vazio"
          description="Os produtos que você adicionar ficam aqui até a hora do pedido, mesmo se fechar a loja."
          action={
            <Link href="/ofertas" className={buttonVariants()}>
              Ver as ofertas
            </Link>
          }
        />
      </main>
    );
  }

  const { lines, unreachable } = await loadCartLines(cart);
  const summary = summarize(lines);
  const priceChanges = lines.filter((l) => lineIssue(l) === "price_changed").length;

  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
      <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">Carrinho</h1>
      {undo}

      {recheck && (
        <section
          role="alert"
          aria-labelledby="conferir"
          className="border-ink bg-surface mt-4 rounded-md border-l-4 p-4"
        >
          <h2 id="conferir" className="font-semibold">
            O pedido não foi feito: algo mudou no carrinho
          </h2>
          <p className="text-ink-soft text-sm">
            Um preço mudou ou um produto saiu de venda desde que você revisou. Nada foi cobrado. Confira os
            avisos abaixo e continue para o pagamento de novo.
          </p>
        </section>
      )}

      {priceChanges > 0 && (
        <section
          aria-labelledby="precos"
          className="border-ink bg-surface mt-4 flex flex-col gap-3 rounded-md border-l-4 p-4 sm:flex-row sm:items-center sm:justify-between"
        >
          <div>
            <h2 id="precos" className="font-semibold">
              {priceChanges === 1
                ? "O preço de 1 produto mudou desde que você o adicionou"
                : `O preço de ${priceChanges} produtos mudou desde que você os adicionou`}
            </h2>
            <p className="text-ink-soft text-sm">
              Confira os valores marcados abaixo. O pedido só sai com os preços que você aceitar.
            </p>
          </div>
          <form action={acceptCartPrices}>
            <SubmitButton variant="primary" size="sm" pendingLabel="Atualizando…">
              Aceitar os novos preços
            </SubmitButton>
          </form>
        </section>
      )}

      <div className="mt-6 grid gap-10 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <ul aria-label="Produtos no carrinho" className="divide-line border-line divide-y border-y">
          {lines.map((line) => (
            <CartLineItem key={line.id} line={line} unreachable={unreachable} />
          ))}
        </ul>

        <aside aria-labelledby="resumo" className="lg:sticky lg:top-6 lg:self-start">
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
            <div className="border-line flex items-end justify-between gap-4 border-t pt-3">
              <dt className="font-semibold">Total</dt>
              <dd className="font-condensed text-3xl leading-none font-extrabold">{brl(summary.total)}</dd>
            </div>
          </dl>
          {summary.savings > 0 && (
            <p className="text-success mt-2 text-right text-sm font-semibold">
              Você economiza {brl(summary.savings)}
            </p>
          )}

          <div className="mt-6 space-y-3">
            {summary.canCheckout ? (
              <Link href="/checkout" prefetch={false} className={cn(buttonVariants(), "w-full")}>
                Continuar para o pagamento
              </Link>
            ) : (
              <>
                <p id="bloqueio" className="text-ink-soft text-sm">
                  {summary.units === 0
                    ? "Nenhum produto do carrinho está disponível agora."
                    : "Resolva os avisos dos produtos para continuar."}
                </p>
                <button
                  type="button"
                  disabled
                  aria-describedby="bloqueio"
                  className={cn(buttonVariants(), "w-full")}
                >
                  Continuar para o pagamento
                </button>
              </>
            )}
            <Link href="/" className={cn(buttonVariants({ variant: "ghost" }), "w-full")}>
              Continuar comprando
            </Link>
          </div>
        </aside>
      </div>
    </main>
  );
}

function CartLineItem({ line, unreachable }: { line: CartLine; unreachable: boolean }) {
  const { product } = line;
  const issue = lineIssue(line);
  const productId = <input type="hidden" name="product_id" value={line.id} />;
  const stepper =
    "text-ink hover:bg-ink/8 grid size-11 cursor-pointer place-items-center rounded-md disabled:cursor-not-allowed disabled:opacity-40 [&_svg]:size-4";
  const maxQty = Math.min(MAX_QUANTITY, line.available && line.available > 0 ? line.available : MAX_QUANTITY);

  if (!product) {
    return (
      <li className="flex flex-wrap items-center justify-between gap-3 py-4">
        <p className="text-ink-soft">
          {unreachable
            ? "Um produto não carregou agora. Recarregue a página para ver o preço."
            : "Um produto saiu do catálogo e não pode mais ser comprado."}
        </p>
        <form action={removeFromCart}>
          {productId}
          <SubmitButton variant="secondary" size="sm" pendingLabel="Removendo…">
            Remover
          </SubmitButton>
        </form>
      </li>
    );
  }

  const unit = product.effective_price.amount_cents;
  const lineTotal = unit * line.qty;

  return (
    <li className="grid grid-cols-[5rem_minmax(0,1fr)] gap-x-4 gap-y-3 py-5 sm:grid-cols-[6rem_minmax(0,1fr)_auto]">
      <Link
        href={`/produto/${product.id}`}
        tabIndex={-1}
        aria-hidden="true"
        className="row-span-2 sm:row-span-1"
      >
        <ProductImage image={product.image} alt="" categorySlug={product.category.slug} sizes="96px" />
      </Link>

      <div className="min-w-0 space-y-1">
        <h3 className="leading-snug font-medium">
          <Link href={`/produto/${product.id}`} className="hover:underline">
            {product.name}
          </Link>
        </h3>
        <p className="text-ink-soft text-sm">
          {brl(unit)} cada
          {product.price.amount_cents > unit && (
            <>
              {" "}
              <s className="ml-1">{brl(product.price.amount_cents)}</s>
            </>
          )}
        </p>
        {issue && <LineNotice issue={issue} line={line} />}
      </div>

      <div className="col-start-2 flex flex-wrap items-center justify-between gap-3 sm:col-start-3 sm:row-start-1 sm:flex-col sm:items-end">
        <p
          className={cn(
            "font-condensed order-last text-xl font-extrabold sm:order-none",
            issue === "sold_out" && "text-ink-soft line-through",
          )}
        >
          <span className="sr-only">Subtotal: </span>
          {brl(lineTotal)}
        </p>
        <div className="flex items-center gap-1">
          {/* − e + são botões de envio: funcionam sem JavaScript */}
          <form action={updateQuantity} className="flex items-center">
            {productId}
            <button
              type="submit"
              name="quantity"
              value={line.qty - 1}
              disabled={line.qty <= 1}
              className={stepper}
              aria-label={`Diminuir a quantidade de ${product.name}`}
            >
              <Minus aria-hidden="true" />
            </button>
            <output
              aria-label={`Quantidade de ${product.name}`}
              className="font-condensed w-8 text-center text-lg font-bold"
            >
              {line.qty}
            </output>
            <button
              type="submit"
              name="quantity"
              value={line.qty + 1}
              disabled={line.qty >= maxQty}
              className={stepper}
              aria-label={`Aumentar a quantidade de ${product.name}`}
            >
              <Plus aria-hidden="true" />
            </button>
          </form>
          <form action={removeFromCart}>
            {productId}
            <button type="submit" className={stepper} aria-label={`Remover ${product.name} do carrinho`}>
              <Trash2 aria-hidden="true" />
            </button>
          </form>
        </div>
      </div>
    </li>
  );
}

function LineNotice({ issue, line }: { issue: NonNullable<ReturnType<typeof lineIssue>>; line: Line }) {
  if (issue === "price_changed" && line.unit) {
    const up = line.unit.effective > line.seen;
    return (
      <p className="text-sm font-semibold">
        {up ? "Ficou mais caro" : "Ficou mais barato"}: era {brl(line.seen)}, agora {brl(line.unit.effective)}
      </p>
    );
  }
  if (issue === "sold_out") {
    return <p className="text-danger text-sm font-semibold">Esgotado. Remova para continuar.</p>;
  }
  if (issue === "short") {
    return (
      <form action={updateQuantity} className="flex flex-wrap items-center gap-2">
        <input type="hidden" name="product_id" value={line.id} />
        <input type="hidden" name="quantity" value={line.available} />
        <p className="text-danger text-sm font-semibold">
          {line.available === 1 ? "Só resta 1 unidade." : `Só restam ${line.available} unidades.`}
        </p>
        <SubmitButton variant="secondary" size="sm" pendingLabel="Ajustando…">
          Ficar com {line.available}
        </SubmitButton>
      </form>
    );
  }
  return null;
}
