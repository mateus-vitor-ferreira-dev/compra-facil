import type { Brand, CardField, CheckedCard } from "./card";

/** O provedor de pagamento, como a página o recebe do servidor: o endereço e a chave pública. */
export type PaymentProvider = { url: string; publicKey: string };

export type TokenResult =
  { ok: true; token: string; brand: Brand } | { ok: false; field?: CardField; message: string };

// o provedor diz qual dado recusou ("invalid card: validade"); a tela põe o erro no campo certo
const REJECTED: [RegExp, CardField, string][] = [
  [/número/, "number", "O provedor não reconheceu este número de cartão."],
  [/código de segurança/, "cvv", "O código de segurança não confere."],
  [/validade/, "expiry", "A validade não confere ou o cartão já venceu."],
  [/titular/, "name", "Digite o nome como está impresso no cartão."],
];

/**
 * Troca o cartão por um token de uso único direto no provedor (o `POST /v1/card_tokens` do Mercado
 * Pago, com a chave pública). Só o token, a bandeira e as parcelas vão para a loja.
 */
export async function tokenizeCard(provider: PaymentProvider, card: CheckedCard): Promise<TokenResult> {
  const url = new URL("/v1/card_tokens", provider.url);
  url.searchParams.set("public_key", provider.publicKey);
  let response: Response;
  try {
    response = await fetch(url, {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        card_number: card.number,
        security_code: card.cvv,
        expiration_month: card.month,
        expiration_year: card.year,
        cardholder: { name: card.name },
      }),
    });
  } catch {
    return { ok: false, message: "O cartão não foi conferido: sem conexão com o provedor. Tente de novo." };
  }
  const body = (await response.json().catch(() => null)) as {
    id?: string;
    payment_method_id?: string;
    message?: string;
  } | null;
  if (response.ok && body?.id) {
    return { ok: true, token: body.id, brand: (body.payment_method_id as Brand | undefined) ?? card.brand };
  }
  if (response.status === 400) {
    const match = REJECTED.find(([pattern]) => pattern.test(body?.message ?? ""));
    if (match) return { ok: false, field: match[1], message: match[2] };
  }
  return {
    ok: false,
    message:
      "O provedor de pagamento não conferiu o cartão agora. Nada foi cobrado. Tente de novo em instantes.",
  };
}
