import {
  MAX_ITEMS,
  acceptPrices,
  addItem,
  emptyCart,
  itemCount,
  lineIssue,
  removeItem,
  setQuantity,
  summarize,
  type Cart,
} from "./cart-rules";

const A = "0199f6a0-0000-7000-8000-00000000000a";
const B = "0199f6a0-0000-7000-8000-00000000000b";

describe("addItem", () => {
  it("soma ao que já estava e guarda o preço visto nesta adição", () => {
    const first = addItem(emptyCart(), A, 2, 1000);
    if (!first.ok) throw new Error();
    const again = addItem(first.cart, A, 3, 900);
    expect(again).toMatchObject({
      ok: true,
      qty: 5,
      limited: false,
      cart: { items: [{ id: A, qty: 5, seen: 900 }] },
    });
  });

  it("para no estoque disponível e no limite de 99, e avisa que limitou", () => {
    expect(addItem(emptyCart(), A, 10, 1000, 3)).toMatchObject({ ok: true, qty: 3, limited: true });
    expect(addItem(emptyCart(), A, 150, 1000)).toMatchObject({ ok: true, qty: 99, limited: true });
  });

  it("recusa esgotado e o 51º produto diferente", () => {
    expect(addItem(emptyCart(), A, 1, 1000, 0)).toEqual({ ok: false, reason: "sold_out" });
    const full: Cart = {
      items: Array.from({ length: MAX_ITEMS }, (_, i) => ({ id: `id-${i}`, qty: 1, seen: 100 })),
    };
    expect(addItem(full, A, 1, 1000)).toEqual({ ok: false, reason: "full" });
    // um que já está continua aceitando unidades
    expect(addItem(full, "id-0", 1, 100)).toMatchObject({ ok: true, qty: 2 });
  });
});

describe("quantidade e remoção", () => {
  const cart: Cart = {
    items: [
      { id: A, qty: 2, seen: 1000 },
      { id: B, qty: 1, seen: 500 },
    ],
  };

  it("setQuantity limita a 1..99 e 0 tira o produto", () => {
    expect(setQuantity(cart, A, 120).items[0]?.qty).toBe(99);
    expect(setQuantity(cart, A, 0).items.map((i) => i.id)).toEqual([B]);
    expect(removeItem(cart, B).items.map((i) => i.id)).toEqual([A]);
    expect(itemCount(cart)).toBe(3);
  });

  it("aceitar os preços troca o visto pelo de agora (e deixa quem não respondeu como está)", () => {
    expect(acceptPrices(cart, { [A]: 1100 }).items).toEqual([
      { id: A, qty: 2, seen: 1100 },
      { id: B, qty: 1, seen: 500 },
    ]);
  });
});

describe("linhas e resumo", () => {
  const ok = { id: A, qty: 2, seen: 800, unit: { price: 1000, effective: 800 }, available: 10 };

  it("aponta o problema de cada linha", () => {
    expect(lineIssue(ok)).toBeUndefined();
    expect(lineIssue({ ...ok, unit: undefined })).toBe("removed");
    expect(lineIssue({ ...ok, available: 0 })).toBe("sold_out");
    expect(lineIssue({ ...ok, available: 1 })).toBe("short");
    expect(lineIssue({ ...ok, seen: 900 })).toBe("price_changed");
    // sem resposta do estoque não é problema: o pedido confere
    expect(lineIssue({ ...ok, available: undefined })).toBeUndefined();
  });

  it("soma tabela, desconto e total, fora o que não dá para comprar", () => {
    const sold = { id: B, qty: 1, seen: 500, unit: { price: 500, effective: 500 }, available: 0 };
    expect(summarize([ok, sold])).toEqual({
      list: 2000,
      total: 1600,
      savings: 400,
      units: 2,
      issues: ["sold_out"],
      canCheckout: false,
    });
    expect(summarize([ok]).canCheckout).toBe(true);
  });
});
