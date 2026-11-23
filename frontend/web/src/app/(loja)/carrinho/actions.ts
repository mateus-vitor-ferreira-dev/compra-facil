"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { cartFits, readCart, readUndo, writeCart, writeUndo } from "@/lib/cart";
import {
  MAX_ITEMS,
  MAX_LINE_KEY,
  acceptPrices,
  addItem,
  emptyCart,
  lineKey,
  removeItem,
  setQuantity,
} from "@/lib/cart-rules";
import { getProduct } from "@/lib/catalog-api";
import { getAvailability } from "@/lib/inventory-api";
import { normalizePersonalization, personalizationError } from "@/lib/personalization";

/**
 * O que muda o carrinho. São server actions (POST com a checagem de origem do Next) chamadas por
 * formulários comuns: funcionam sem JavaScript, e gravar o cookie já atualiza a página e o cabeçalho.
 */

export type AddState =
  | { status: "idle" }
  | {
      status: "added";
      name: string;
      qty: number;
      limited: boolean;
      size?: string;
      color?: string;
      text?: string;
    }
  | { status: "error"; message: string };

const id = z.uuid();
const quantity = z.coerce.number().int();
// a linha do carrinho: o id do produto e a variante (lineKey)
const line = z.string().min(1).max(MAX_LINE_KEY);

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
  // o tamanho como o catálogo o cadastra; o produto com tamanho exige a escolha
  const sizes = product.data.sizes ?? [];
  const wantedSize = String(form.get("size") ?? "").trim();
  const size = sizes.find((s) => s.toUpperCase() === wantedSize.toUpperCase());
  if (sizes.length > 0 && !size) {
    return { status: "error", message: "Escolha o tamanho." };
  }
  // a cor como o catálogo a cadastra; o produto com cor à escolha exige a escolha
  const colors = product.data.colors ?? [];
  const wantedColor = String(form.get("color") ?? "").trim();
  const color = colors.find((c) => c.toUpperCase() === wantedColor.toUpperCase());
  if (colors.length > 0 && !color) {
    return { status: "error", message: "Escolha a cor." };
  }
  // o texto da personalização, normalizado e conferido com a regra do produto
  const text = normalizePersonalization(String(form.get("personalization") ?? "").slice(0, 200));
  const textError = personalizationError(text, product.data.personalization);
  if (textError) return { status: "error", message: textError };
  // o preço que o cliente viu na página; se mudou desde então, o carrinho avisa
  const seen = quantity.safeParse(form.get("seen_price_cents"));
  const price = seen.success && seen.data > 0 ? seen.data : product.data.effective_price.amount_cents;

  const result = addItem(
    await readCart(),
    productId.data,
    qty.data,
    price,
    stock.ok ? stock.data[productId.data] : undefined,
    size,
    text || undefined,
    color,
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
  if (!cartFits(result.cart)) {
    return {
      status: "error",
      message: "O carrinho está cheio: finalize este pedido e faça outro com o restante.",
    };
  }
  await writeCart(result.cart);
  return {
    status: "added",
    name: product.data.name,
    qty: result.qty,
    limited: result.limited,
    size,
    color,
    text: text || undefined,
  };
}

export async function updateQuantity(form: FormData): Promise<void> {
  const productId = id.safeParse(form.get("product_id"));
  const key = line.safeParse(form.get("line"));
  const qty = quantity.safeParse(form.get("quantity"));
  if (!productId.success || !key.success || !qty.success) return;
  const stock = await getAvailability([productId.data]);
  const available = stock.ok ? stock.data[productId.data] : undefined;
  // não passa do disponível; com o estoque zerado, a linha mostra "esgotado" e a opção de remover
  const capped = available !== undefined && available > 0 ? Math.min(qty.data, available) : qty.data;
  await writeCart(setQuantity(await readCart(), key.data, capped));
}

export async function removeFromCart(form: FormData): Promise<void> {
  const key = line.safeParse(form.get("line"));
  if (!key.success) return;
  const cart = await readCart();
  await writeUndo(cart.items.find((i) => lineKey(i) === key.data));
  await writeCart(removeItem(cart, key.data));
}

/**
 * "Esvaziar o carrinho" (#321): tira todos os produtos de uma vez, depois da confirmação na página. Sem
 * desfazer: o desfazer guarda um produto só, e a confirmação já evita o engano.
 */
export async function clearCart(): Promise<void> {
  await writeUndo(undefined);
  await writeCart(emptyCart());
  redirect("/carrinho?esvaziado=1");
}

/** "Desfazer": devolve o último item removido, com a quantidade e o preço visto de antes. */
export async function undoRemove(): Promise<void> {
  const item = await readUndo();
  await writeUndo(undefined);
  if (!item) return;
  const cart = await readCart();
  if (cart.items.some((i) => lineKey(i) === lineKey(item)) || cart.items.length >= MAX_ITEMS) return;
  await writeCart({ items: [...cart.items, item] });
}

/** "Aceitar os novos preços": o preço visto de cada item passa a ser o de agora. */
export async function acceptCartPrices(): Promise<void> {
  const cart = await readCart();
  const products = await Promise.all([...new Set(cart.items.map((i) => i.id))].map(getProduct));
  const current = Object.fromEntries(
    products.flatMap((p) => (p.ok ? [[p.data.id, p.data.effective_price.amount_cents]] : [])),
  );
  await writeCart(acceptPrices(cart, current));
}
