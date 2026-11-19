"use client";

import { ShoppingCart } from "lucide-react";
import Link from "next/link";
import { useActionState, useEffect, useRef } from "react";
import { QuantityStepper } from "@/components/commerce/quantity-stepper";
import { Button, buttonVariants } from "@/components/ui/button";
import { useToast } from "@/components/ui/toast";
import { addToCart, type AddState } from "../../carrinho/actions";

/**
 * Quantidade e "Adicionar ao carrinho". Funciona sem JavaScript (form com server action); com ele,
 * mostra o andamento no botão, confirma num aviso e oferece ir ao carrinho.
 */
export function AddToCartForm({
  productId,
  seenPriceCents,
  max,
  sizes = [],
}: {
  productId: string;
  /** o preço que esta página mostrou: se mudar até o pedido, o carrinho avisa */
  seenPriceCents: number;
  max?: number;
  /** os tamanhos à escolha; vazio, o produto não tem tamanho */
  sizes?: string[];
}) {
  const [state, action, pending] = useActionState<AddState, FormData>(addToCart, { status: "idle" });
  const { show } = useToast();
  const last = useRef<AddState>(state);

  useEffect(() => {
    if (state === last.current) return;
    last.current = state;
    if (state.status === "added") {
      show(
        "success",
        state.limited
          ? `Adicionado ao carrinho até o limite disponível: ${state.qty} no total${sizeNote(state.size)}.`
          : `Adicionado ao carrinho: ${state.qty} ${state.qty === 1 ? "unidade" : "unidades"} no total${sizeNote(state.size)}.`,
      );
    }
  }, [state, show]);

  return (
    <form action={action} className="space-y-3">
      <input type="hidden" name="product_id" value={productId} />
      <input type="hidden" name="seen_price_cents" value={seenPriceCents} />
      {sizes.length > 0 && <SizePicker productId={productId} sizes={sizes} />}
      <div className="flex flex-wrap items-end gap-3">
        <QuantityStepper id={`qtd-${productId}`} max={max} />
        <Button type="submit" disabled={pending} className="min-w-0 flex-1 sm:flex-none sm:px-8">
          <ShoppingCart aria-hidden="true" />
          {pending ? "Adicionando…" : "Adicionar ao carrinho"}
        </Button>
      </div>
      {state.status === "error" && (
        <p role="alert" className="text-danger text-sm font-medium">
          {state.message}
        </p>
      )}
      {state.status === "added" && (
        <Link
          href="/carrinho"
          className={buttonVariants({ variant: "secondary", className: "w-full sm:w-auto" })}
        >
          Ver o carrinho
        </Link>
      )}
    </form>
  );
}

const sizeNote = (size?: string) => (size ? ` no tamanho ${size}` : "");

/**
 * Os tamanhos como opções de rádio em botões (44 px no toque): um grupo nativo, que o teclado percorre com as
 * setas e o leitor de tela anuncia como "Tamanho". Nenhum vem marcado: o cliente escolhe, e o navegador não
 * envia o formulário sem a escolha (o servidor confere de novo).
 */
function SizePicker({ productId, sizes }: { productId: string; sizes: string[] }) {
  return (
    <fieldset className="space-y-1">
      <legend className="text-ink-soft text-sm">Tamanho</legend>
      <div className="flex flex-wrap gap-2">
        {sizes.map((size) => (
          <label key={size} className="relative">
            <input
              type="radio"
              name="size"
              value={size}
              required
              id={`tam-${productId}-${size}`}
              className="peer sr-only"
            />
            <span className="border-field-line bg-surface text-ink peer-checked:border-ink peer-checked:bg-ink peer-checked:text-surface peer-focus-visible:outline-focus grid h-11 min-w-11 cursor-pointer place-items-center rounded-md border px-3 font-semibold peer-focus-visible:outline-2 peer-focus-visible:outline-offset-2">
              {size}
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}
