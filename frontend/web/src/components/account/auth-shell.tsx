import type { ReactNode } from "react";
import { readCart } from "@/lib/cart";
import { itemCount } from "@/lib/cart-rules";

/**
 * A moldura das telas de entrada: o formulário numa coluna estreita, alinhado à esquerda. Com produtos
 * no carrinho, lembra que eles continuam guardados (o motivo mais comum para entrar é finalizar).
 */
export async function AuthShell({
  title,
  intro,
  children,
}: {
  title: string;
  intro?: string;
  children: ReactNode;
}) {
  const count = itemCount(await readCart());
  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-8 sm:px-6">
      <div className="max-w-md space-y-6">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight sm:text-3xl">{title}</h1>
          {intro && <p className="text-ink-soft">{intro}</p>}
        </div>
        {count > 0 && (
          <p className="border-ink bg-surface rounded-md border-l-4 px-4 py-3 text-sm">
            {count === 1
              ? "O produto do seu carrinho continua"
              : `Os ${count} produtos do seu carrinho continuam`}{" "}
            guardados: depois de entrar, é só seguir para o pagamento.
          </p>
        )}
        {children}
      </div>
    </main>
  );
}
