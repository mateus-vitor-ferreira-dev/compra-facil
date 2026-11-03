"use server";

import { redirect } from "next/navigation";
import { z } from "zod";
import { readCart, readConfirmedDoubles, writeCart, writeConfirmedDoubles } from "@/lib/cart";
import { setQuantity } from "@/lib/cart-rules";

/**
 * As duas saídas da pergunta das 2 unidades iguais (#322). São formulários comuns: funcionam sem JavaScript, e as
 * duas voltam para o pagamento, que refaz o total.
 */
const ids = (form: FormData) =>
  form
    .getAll("product_id")
    .map((v) => z.uuid().safeParse(v))
    .flatMap((r) => (r.success ? [r.data] : []));

/** "Está certo": as 2 unidades seguem, e a pergunta não volta nesta compra. */
export async function keepDoubles(form: FormData): Promise<void> {
  const confirmed = await readConfirmedDoubles();
  await writeConfirmedDoubles([...new Set([...confirmed, ...ids(form)])]);
  redirect("/checkout");
}

/** "Foi engano": tira uma unidade de cada produto repetido (só de quem ainda tem 2) e segue para o pagamento. */
export async function dropDoubles(form: FormData): Promise<void> {
  let cart = await readCart();
  for (const id of ids(form)) {
    if (cart.items.find((i) => i.id === id)?.qty === 2) cart = setQuantity(cart, id, 1);
  }
  await writeCart(cart);
  redirect("/checkout");
}
