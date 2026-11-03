import Link from "next/link";
import { ProductImage } from "@/components/commerce/product-image";
import { FocusHeading } from "@/components/ui/focus-heading";
import { SubmitButton } from "@/components/ui/submit-button";
import type { CartLine } from "@/lib/cart-lines";
import { formatMoney } from "@/lib/format";
import { dropDoubles, keepDoubles } from "./quantity-actions";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/**
 * A pergunta antes do pagamento quando há 2 unidades do mesmo produto (#322): costuma ser um clique repetido. Um
 * aviso só, com todos os repetidos, e duas saídas de um clique; cada uma mostra o total dos produtos que vai
 * valer. Fica no lugar do formulário de pagamento, então vale para qualquer caminho que chegue ao /checkout.
 */
export function QuantityCheck({ doubles, itemsTotal }: { doubles: CartLine[]; itemsTotal: number }) {
  const one = doubles.length === 1;
  const fewer =
    itemsTotal - doubles.reduce((sum, l) => sum + (l.product?.effective_price.amount_cents ?? 0), 0);
  const productIds = doubles.map((l) => <input key={l.id} type="hidden" name="product_id" value={l.id} />);

  return (
    <section
      aria-labelledby="quantidade"
      className="border-ink bg-surface mt-6 max-w-xl space-y-5 rounded-md border-2 p-5"
    >
      <div className="space-y-2">
        <FocusHeading level={1} id="quantidade" className="text-2xl tracking-tight sm:text-3xl">
          {one ? `2 unidades de ${doubles[0]?.product?.name}?` : "Confira as quantidades"}
        </FocusHeading>
        <p className="text-ink-soft">
          {one
            ? "O carrinho tem 2 unidades iguais deste produto. Confirme antes de seguir para o pagamento."
            : "O carrinho tem 2 unidades iguais de cada um destes produtos. Confirme antes de seguir para o pagamento."}
        </p>
      </div>

      <ul className="divide-line border-line divide-y border-y">
        {doubles.map(({ id, product }) =>
          product ? (
            <li key={id} className="grid grid-cols-[3.5rem_minmax(0,1fr)] items-center gap-3 py-3">
              <ProductImage image={product.image} alt="" categorySlug={product.category.slug} sizes="56px" />
              <div className="min-w-0">
                <p className="leading-snug font-medium">{product.name}</p>
                <p className="text-ink-soft text-sm">2 × {brl(product.effective_price.amount_cents)}</p>
              </div>
            </li>
          ) : null,
        )}
      </ul>

      <div className="grid gap-3 sm:grid-cols-2">
        <form action={keepDoubles} className="space-y-1">
          {productIds}
          <SubmitButton className="w-full" pendingLabel="Seguindo…">
            {one ? "Está certo, quero 2" : "Está certo, 2 de cada"}
          </SubmitButton>
          <p className="text-ink-soft text-center text-sm">Produtos: {brl(itemsTotal)}</p>
        </form>
        <form action={dropDoubles} className="space-y-1">
          {productIds}
          <SubmitButton variant="secondary" className="w-full" pendingLabel="Tirando…">
            {one ? "Foi engano, quero 1" : "Foi engano, 1 de cada"}
          </SubmitButton>
          <p className="text-ink-soft text-center text-sm">Produtos: {brl(fewer)}</p>
        </form>
      </div>

      {!one && (
        <p className="text-sm">
          Quer mudar só um deles?{" "}
          <Link href="/carrinho" className="font-semibold underline underline-offset-4">
            Alterar o carrinho
          </Link>
        </p>
      )}
    </section>
  );
}
