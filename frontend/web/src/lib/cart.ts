import "server-only";
import { cookies } from "next/headers";
import { z } from "zod";
import { env } from "@/lib/env";
import { MAX_ITEMS, MAX_QUANTITY, emptyCart, type Cart, type CartItem } from "./cart-rules";

/**
 * O carrinho mora num cookie httpOnly (sem conta, sem serviço novo): ids, quantidades e o preço que o
 * cliente viu. Até 50 itens cabem com folga nos 4 KB de um cookie. O conteúdo não é confiável: o pedido
 * revalida produto, preço e estoque, e um cookie adulterado só vira um carrinho vazio.
 */
export const CART_COOKIE = "cf_cart";
const MAX_AGE = 30 * 24 * 3600;

const schema = z.object({
  items: z
    .array(
      z.object({
        id: z.uuid(),
        qty: z.int().min(1).max(MAX_QUANTITY),
        seen: z.int().min(1),
      }),
    )
    .max(MAX_ITEMS),
});

export async function readCart(): Promise<Cart> {
  const raw = (await cookies()).get(CART_COOKIE)?.value;
  if (!raw) return emptyCart();
  try {
    const parsed = schema.safeParse(JSON.parse(Buffer.from(raw, "base64url").toString("utf8")));
    return parsed.success ? parsed.data : emptyCart();
  } catch {
    return emptyCart();
  }
}

/** O último item removido, por 2 minutos: o "Desfazer" do carrinho. */
export const UNDO_COOKIE = "cf_cart_undo";
const undoSchema = schema.shape.items.element;

export async function readUndo(): Promise<CartItem | undefined> {
  const raw = (await cookies()).get(UNDO_COOKIE)?.value;
  if (!raw) return undefined;
  try {
    const parsed = undoSchema.safeParse(JSON.parse(Buffer.from(raw, "base64url").toString("utf8")));
    return parsed.success ? parsed.data : undefined;
  } catch {
    return undefined;
  }
}

export async function writeUndo(item: CartItem | undefined): Promise<void> {
  const store = await cookies();
  if (!item) {
    store.delete(UNDO_COOKIE);
    return;
  }
  store.set(UNDO_COOKIE, Buffer.from(JSON.stringify(item)).toString("base64url"), {
    httpOnly: true,
    secure: env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/carrinho",
    maxAge: 120,
  });
}

/** Só em server action ou route handler (onde o Next deixa gravar cookie). */
export async function writeCart(cart: Cart): Promise<void> {
  const store = await cookies();
  if (cart.items.length === 0) {
    store.delete(CART_COOKIE);
    return;
  }
  store.set(CART_COOKIE, Buffer.from(JSON.stringify(cart)).toString("base64url"), {
    httpOnly: true,
    secure: env.NODE_ENV === "production",
    sameSite: "lax",
    path: "/",
    maxAge: MAX_AGE,
  });
}
