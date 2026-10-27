"use server";

import { z } from "zod";
import { readCart, readUndo, writeCart, writeUndo } from "@/lib/cart";
import { MAX_ITEMS, acceptPrices, addItem, removeItem, setQuantity } from "@/lib/cart-rules";
import { getProduct } from "@/lib/catalog-api";
import { getAvailability } from "@/lib/inventory-api";

/**
 * O que muda o carrinho. São server actions (POST com a checagem de origem do Next) chamadas por
 * formulários comuns: funcionam sem JavaScript, e gravar o cookie já atualiza a página e o cabeçalho.
 */

export type AddState =
  | { status: "idle" }
  | { status: "added"; name: string; qty: number; limited: boolean }
  | { status: "error"; message: string };

const id = z.uuid();
const quantity = z.coerce.number().int();

export async function addToCart(_: AddState, form: FormData): Promise<AddState> {
  const productId = id.safeParse(form.get("product_id"));
  const qty = quantity.safeParse(form.get("quantity"));
  if (!productId.success || !qty.success || qty.data < 1) {
    return { status: "error", message: "Escolha uma quantidade de 1 a 99." };
  }
  const [product, stock] = await Promise.all([getProduct(productId.data), getAvailability([productId.data])]);
  if (!product.ok) {
    return {
      status: "error",
      message:
        product.error.status === 404
          ? "Este produto saiu do catálogo."
          : "O produto não foi adicionado: a loja não respondeu. Tente de novo.",
    };
  }
  // o preço que o cliente viu na página; se mudou desde então, o carrinho avisa
  const seen = quantity.safeParse(form.get("seen_price_cents"));
  const price = seen.success && seen.data > 0 ? seen.data : product.data.effective_price.amount_cents;

  const result = addItem(
    await readCart(),
    productId.data,
    qty.data,
    price,
    stock.ok ? stock.data[productId.data] : undefined,
  );
  if (!result.ok) {
    return {
      status: "error",
      message:
        result.reason === "sold_out"
          ? "Este produto esgotou."
          : `O carrinho já tem ${MAX_ITEMS} produtos diferentes, o máximo de um pedido.`,
    };
  }
  await writeCart(result.cart);
  return { status: "added", name: product.data.name, qty: result.qty, limited: result.limited };
}

export async function updateQuantity(form: FormData): Promise<void> {
  const productId = id.safeParse(form.get("product_id"));
  const qty = quantity.safeParse(form.get("quantity"));
  if (!productId.success || !qty.success) return;
  const stock = await getAvailability([productId.data]);
  const available = stock.ok ? stock.data[productId.data] : undefined;
  // não passa do disponível; com o estoque zerado, a linha mostra "esgotado" e a opção de remover
  const capped = available !== undefined && available > 0 ? Math.min(qty.data, available) : qty.data;
  await writeCart(setQuantity(await readCart(), productId.data, capped));
}

export async function removeFromCart(form: FormData): Promise<void> {
  const productId = id.safeParse(form.get("product_id"));
  if (!productId.success) return;
  const cart = await readCart();
  await writeUndo(cart.items.find((i) => i.id === productId.data));
  await writeCart(removeItem(cart, productId.data));
}

/** "Desfazer": devolve o último item removido, com a quantidade e o preço visto de antes. */
export async function undoRemove(): Promise<void> {
  const item = await readUndo();
  await writeUndo(undefined);
  if (!item) return;
  const cart = await readCart();
  if (cart.items.some((i) => i.id === item.id) || cart.items.length >= MAX_ITEMS) return;
  await writeCart({ items: [...cart.items, item] });
}

/** "Aceitar os novos preços": o preço visto de cada item passa a ser o de agora. */
export async function acceptCartPrices(): Promise<void> {
  const cart = await readCart();
  const products = await Promise.all(cart.items.map((i) => getProduct(i.id)));
  const current = Object.fromEntries(
    products.flatMap((p) => (p.ok ? [[p.data.id, p.data.effective_price.amount_cents]] : [])),
  );
  await writeCart(acceptPrices(cart, current));
}
