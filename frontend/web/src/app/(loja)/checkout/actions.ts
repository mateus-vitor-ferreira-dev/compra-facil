"use server";

import { updateTag } from "next/cache";
import { cookies } from "next/headers";
import { redirect } from "next/navigation";
import { z } from "zod";
import { accessToken } from "@/lib/auth/current";
import { loginHref } from "@/lib/auth/return-to";
import { readCart, writeCart } from "@/lib/cart";
import { emptyCart } from "@/lib/cart-rules";
import {
  checkDelivery,
  DELIVERY_FIELDS,
  toOrderDelivery,
  type DeliveryErrors,
  type DeliveryInput,
} from "@/lib/delivery";
import { env } from "@/lib/env";
import { placeOrder, type PlaceOrderPayment } from "@/lib/orders-api";

export type CheckoutState = {
  /** a entrega recusada: os erros por campo e o que o cliente digitou (sem JavaScript, a página recarrega) */
  delivery?: { errors: DeliveryErrors; values: DeliveryInput };
  error?: {
    title: string;
    message: string;
    /** o código que o cliente passa ao suporte (o trace da requisição) */
    traceId?: string;
  };
};

/**
 * O último pedido feito por este navegador, pela Idempotency-Key: o segundo envio do mesmo formulário
 * (clique duplo sem JavaScript, voltar e reenviar) acha o carrinho já vazio e vai para o pedido certo.
 */
const LAST_ORDER_COOKIE = "cf_last_order";
const lastOrder = z.object({ key: z.uuid(), id: z.uuid() });

const orderHref = (id: string) => `/conta/pedidos/${id}?novo=1`;

/** O meio de pagamento do formulário: PIX, ou o token do cartão que a página pegou no provedor. */
const paymentForm = z.discriminatedUnion("method", [
  z.object({ method: z.literal("pix") }),
  z.object({
    method: z.literal("card"),
    card_token: z.string().min(1).max(128),
    card_brand: z.string().min(1).max(16),
    installments: z.coerce.number().int().min(1).max(12),
  }),
]);

function readPayment(form: FormData): PlaceOrderPayment | CheckoutState {
  const parsed = paymentForm.safeParse({
    method: form.get("method") ?? "pix",
    card_token: form.get("card_token") || undefined,
    card_brand: form.get("card_brand") || undefined,
    installments: form.get("installments") ?? undefined,
  });
  if (parsed.success) return parsed.data.method === "pix" ? { method: "pix", installments: 1 } : parsed.data;
  // sem JavaScript, a página não tokeniza o cartão: o formulário chega com "cartão" e sem token
  return {
    error: {
      title: "O pedido não foi feito",
      message:
        form.get("method") === "card"
          ? "O cartão é conferido pelo navegador, e esta página está sem JavaScript. Ative o JavaScript ou pague com PIX."
          : "Escolha como pagar e confirme de novo.",
    },
  };
}

export async function confirmOrder(_: CheckoutState, form: FormData): Promise<CheckoutState> {
  const key = z.uuid().safeParse(form.get("idempotency_key"));
  if (!key.success) {
    return {
      error: {
        title: "O pedido não foi feito",
        message: "A página do pagamento ficou velha. Recarregue e confirme de novo.",
      },
    };
  }
  const values = Object.fromEntries(
    DELIVERY_FIELDS.map((f) => [f, String(form.get(f) ?? "")]),
  ) as DeliveryInput;
  const deliveryErrors = checkDelivery(values);
  if (Object.keys(deliveryErrors).length > 0) {
    return {
      delivery: { errors: deliveryErrors, values },
      error: { title: "O pedido não foi feito", message: "Confira os dados de entrega marcados." },
    };
  }
  const shipping = z.string().min(1).max(32).safeParse(form.get("shipping_service"));
  if (!shipping.success) {
    return {
      delivery: { errors: {}, values },
      error: {
        title: "O pedido não foi feito",
        message: "Escolha o frete. Sem JavaScript, ele aparece com o CEP do seu último pedido.",
      },
    };
  }
  const payment = readPayment(form);
  if (!("method" in payment)) return payment;
  const token = await accessToken();
  if (!token) redirect(loginHref("/checkout"));

  const store = await cookies();
  const cart = await readCart();
  if (cart.items.length === 0) {
    const previous = lastOrder.safeParse(parseCookie(store.get(LAST_ORDER_COOKIE)?.value));
    redirect(previous.success && previous.data.key === key.data ? orderHref(previous.data.id) : "/carrinho");
  }

  const result = await placeOrder(
    token,
    cart.items.map((i) => ({ product_id: i.id, quantity: i.qty, expected_unit_price_cents: i.seen })),
    payment,
    { ...toOrderDelivery(values), shipping: { service: shipping.data } },
    key.data,
  );

  if (result.ok) {
    await writeCart(emptyCart());
    store.set(LAST_ORDER_COOKIE, JSON.stringify({ key: key.data, id: result.order.id }), {
      httpOnly: true,
      secure: env.NODE_ENV === "production",
      sameSite: "lax",
      path: "/checkout",
      maxAge: 600,
    });
    redirect(orderHref(result.order.id));
  }

  const { status, retryAfter, traceId } = result.error;
  switch (status) {
    case 401:
      redirect(loginHref("/checkout"));
    case 409:
      // um preço ou um produto mudou: o carrinho relê o catálogo (sem o cache) e mostra o quê
      for (const item of cart.items) updateTag(`catalog:product:${item.id}`);
      redirect("/carrinho?conferir=1");
    case 503:
      return {
        error: {
          title: "O pedido não foi feito",
          message: `A loja não conseguiu conferir os preços agora. Nada foi cobrado. Tente de novo em ${retryAfter ?? 5} segundos.`,
          traceId,
        },
      };
    case 422:
      return {
        error: {
          title: "O pedido não foi feito",
          message:
            "A loja recusou os itens do pedido. Volte ao carrinho, confira as quantidades e tente de novo.",
          traceId,
        },
      };
    default:
      return {
        error: {
          title: "O pedido não foi feito",
          message:
            "A loja não conseguiu registrar o pedido agora. Nada foi cobrado. Tente de novo em instantes.",
          traceId,
        },
      };
  }
}

function parseCookie(raw: string | undefined): unknown {
  if (!raw) return undefined;
  try {
    return JSON.parse(raw);
  } catch {
    return undefined;
  }
}
