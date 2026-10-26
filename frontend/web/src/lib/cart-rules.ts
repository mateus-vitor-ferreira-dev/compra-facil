/**
 * Regras do carrinho, sem servidor nem cookie: o que cabe (até 50 produtos, 1 a 99 de cada, como o
 * pedido aceita) e o preço que o cliente viu, que vai como `expected_unit_price_cents` no pedido.
 */
export const MAX_ITEMS = 50;
export const MAX_QUANTITY = 99;

/** Um produto no carrinho: id, quantidade e o preço unitário (centavos) que o cliente aceitou. */
export type CartItem = { id: string; qty: number; seen: number };
export type Cart = { items: CartItem[] };

export const emptyCart = (): Cart => ({ items: [] });

const clampQty = (qty: number, max = MAX_QUANTITY) =>
  Math.max(1, Math.min(Math.floor(qty) || 1, max, MAX_QUANTITY));

export type AddResult =
  { ok: true; cart: Cart; qty: number; limited: boolean } | { ok: false; reason: "full" | "sold_out" };

/**
 * Soma ao que já está no carrinho, até o estoque disponível (quando se sabe) e o limite de 99. O preço
 * visto passa a ser o desta adição: foi o que o cliente acabou de ver na página.
 */
export function addItem(cart: Cart, id: string, qty: number, seen: number, available?: number): AddResult {
  if (available !== undefined && available <= 0) return { ok: false, reason: "sold_out" };
  const current = cart.items.find((i) => i.id === id);
  if (!current && cart.items.length >= MAX_ITEMS) return { ok: false, reason: "full" };
  const wanted = (current?.qty ?? 0) + Math.max(1, Math.floor(qty) || 1);
  const final = clampQty(wanted, available);
  const items = current
    ? cart.items.map((i) => (i.id === id ? { id, qty: final, seen } : i))
    : [...cart.items, { id, qty: final, seen }];
  return { ok: true, cart: { items }, qty: final, limited: final < wanted };
}

/** Troca a quantidade (0 tira o produto). */
export function setQuantity(cart: Cart, id: string, qty: number): Cart {
  if (qty <= 0) return removeItem(cart, id);
  return { items: cart.items.map((i) => (i.id === id ? { ...i, qty: clampQty(qty) } : i)) };
}

export function removeItem(cart: Cart, id: string): Cart {
  return { items: cart.items.filter((i) => i.id !== id) };
}

/** O cliente aceitou os preços de agora: o preço visto de cada item vira o atual. */
export function acceptPrices(cart: Cart, current: Record<string, number>): Cart {
  return {
    items: cart.items.map((i) => {
      const price = current[i.id];
      return price ? { ...i, seen: price } : i;
    }),
  };
}

export const itemCount = (cart: Cart) => cart.items.reduce((n, i) => n + i.qty, 0);

/** Uma linha do carrinho já com o produto e o estoque, para a tela e o resumo. */
export type Line = {
  id: string;
  qty: number;
  seen: number;
  /** nulo: o produto saiu do catálogo */
  unit?: { price: number; effective: number };
  /** indefinido: o estoque não respondeu (o pedido confere de novo) */
  available?: number;
};

export type LineIssue = "removed" | "sold_out" | "short" | "price_changed";

export function lineIssue(line: Line): LineIssue | undefined {
  if (!line.unit) return "removed";
  if (line.available !== undefined && line.available <= 0) return "sold_out";
  if (line.available !== undefined && line.available < line.qty) return "short";
  if (line.unit.effective !== line.seen) return "price_changed";
  return undefined;
}

/** Totais do que dá para comprar agora (sem os itens que saíram do catálogo ou esgotaram). */
export function summarize(lines: Line[]) {
  let list = 0;
  let total = 0;
  let units = 0;
  for (const line of lines) {
    const issue = lineIssue(line);
    if (!line.unit || issue === "removed" || issue === "sold_out") continue;
    list += line.unit.price * line.qty;
    total += line.unit.effective * line.qty;
    units += line.qty;
  }
  const issues = lines.map(lineIssue).filter(Boolean) as LineIssue[];
  return { list, total, savings: list - total, units, issues, canCheckout: units > 0 && issues.length === 0 };
}
