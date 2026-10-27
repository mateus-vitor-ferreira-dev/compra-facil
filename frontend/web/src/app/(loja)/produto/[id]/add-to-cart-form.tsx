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
}: {
  productId: string;
  /** o preço que esta página mostrou: se mudar até o pedido, o carrinho avisa */
  seenPriceCents: number;
  max?: number;
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
          ? `Adicionado ao carrinho até o limite disponível: ${state.qty} no total.`
          : `Adicionado ao carrinho: ${state.qty} ${state.qty === 1 ? "unidade" : "unidades"} no total.`,
      );
    }
  }, [state, show]);

  return (
    <form action={action} className="space-y-3">
      <input type="hidden" name="product_id" value={productId} />
      <input type="hidden" name="seen_price_cents" value={seenPriceCents} />
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
