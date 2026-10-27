import "server-only";
import type { Cart, Line } from "@/lib/cart-rules";
import type { Product } from "@/lib/catalog";
import { getProduct } from "@/lib/catalog-api";
import { getAvailability } from "@/lib/inventory-api";

export type CartLine = Line & { product?: Product };

/**
 * As linhas do carrinho com o produto e o preço de agora (catálogo) e o estoque (inventory), para o
 * carrinho e o checkout mostrarem o mesmo. `unreachable`: algum produto não respondeu (sem ser 404),
 * então a linha sem produto não é dada como removida.
 */
export async function loadCartLines(cart: Cart): Promise<{ lines: CartLine[]; unreachable: boolean }> {
  const ids = cart.items.map((i) => i.id);
  const [products, stock] = await Promise.all([Promise.all(ids.map(getProduct)), getAvailability(ids)]);
  const byId = new Map<string, Product>(products.flatMap((p) => (p.ok ? [[p.data.id, p.data]] : [])));
  const unreachable = products.some((p) => !p.ok && p.error.status !== 404);

  const lines = cart.items.map((item) => {
    const product = byId.get(item.id);
    return {
      ...item,
      product,
      unit: product
        ? { price: product.price.amount_cents, effective: product.effective_price.amount_cents }
        : undefined,
      available: stock.ok ? stock.data[item.id] : undefined,
    };
  });
  return { lines, unreachable };
}
