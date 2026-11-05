import type { Brand, CardField, CheckedCard } from "./card";

/**
 * O provedor de pagamento, como a página o recebe do servidor (#379): a Efí, com o identificador de conta (é
 * público, vai no navegador). Em `production` e `sandbox`, o cartão é tokenizado pela biblioteca da Efí, que só
 * fala com os hosts dela; em `simulator` (dev, CI e a loja de demonstração), pelo `/sim/card-token` do payment-sim.
 */
export type PaymentProvider = {
  environment: "simulator" | "sandbox" | "production";
  payeeCode: string;
  /** Só no simulador: o endereço do payment-sim. */
  url: string;
};

export type TokenResult =
  { ok: true; token: string; brand: Brand } | { ok: false; field?: CardField; message: string };

/** Os dados do cartão como a Efí os recebe (setCreditCardData). */
type EfiCard = {
  brand: string;
  number: string;
  cvv: string;
  expirationMonth: string;
  expirationYear: string;
  holderName: string;
  holderDocument: string;
  reuse: boolean;
};

type EfiError = { code?: number | string; error?: string; error_description?: unknown };

// a Efí diz qual dado recusou (no código do erro ou no texto); a tela põe o erro no campo certo
const REJECTED: [RegExp, CardField, string][] = [
  [/card_number|número|number|brand|bandeira/i, "number", "O provedor não reconheceu este número de cartão."],
  [/cvv|segurança/i, "cvv", "O código de segurança não confere."],
  [/expiration|validade|vencid|ano|mês/i, "expiry", "A validade não confere ou o cartão já venceu."],
  [/holder|titular|nome/i, "name", "Digite o nome e o sobrenome como estão impressos no cartão."],
];

const UNAVAILABLE =
  "O provedor de pagamento não conferiu o cartão agora. Nada foi cobrado. Tente de novo em instantes.";

/** A Efí chama a bandeira Mastercard de "mastercard". */
const efiBrand = (brand: Brand) => (brand === "master" ? "mastercard" : brand);

const pad2 = (n: number) => String(n).padStart(2, "0");

function toEfi(card: CheckedCard): EfiCard {
  return {
    brand: efiBrand(card.brand),
    number: card.number,
    cvv: card.cvv,
    expirationMonth: pad2(card.month),
    expirationYear: String(card.year),
    holderName: card.name.trim(),
    holderDocument: "",
    reuse: false,
  };
}

function rejected(error: EfiError): TokenResult {
  const text = `${error.error ?? ""} ${typeof error.error_description === "string" ? error.error_description : JSON.stringify(error.error_description ?? "")}`;
  const status = Number(error.code);
  if (status === 400 || /invalid/i.test(error.error ?? "")) {
    const match = REJECTED.find(([pattern]) => pattern.test(text));
    if (match) return { ok: false, field: match[1], message: match[2] };
  }
  return { ok: false, message: UNAVAILABLE };
}

/**
 * Troca o cartão por um token de uso único direto no provedor. Só o token, a bandeira e as parcelas vão para a
 * loja; o número e o código de segurança não passam por ela.
 */
export async function tokenizeCard(provider: PaymentProvider, card: CheckedCard): Promise<TokenResult> {
  return provider.environment === "simulator"
    ? tokenizeOnSimulator(provider, card)
    : tokenizeOnEfi(provider, card);
}

async function tokenizeOnEfi(provider: PaymentProvider, card: CheckedCard): Promise<TokenResult> {
  let result: unknown;
  try {
    // a biblioteca precisa do DOM: carrega só no navegador, na hora de pagar
    const { default: EfiPay } = await import("payment-token-efi");
    result = await EfiPay.CreditCard.setAccount(provider.payeeCode)
      .setEnvironment(provider.environment === "production" ? "production" : "sandbox")
      .setCreditCardData(toEfi(card))
      .getPaymentToken();
  } catch (e) {
    // a biblioteca lança o erro de validação como {code, error, error_description}; o resto é rede
    if (e && typeof e === "object" && "error_description" in e) return rejected(e as EfiError);
    return { ok: false, message: "O cartão não foi conferido: sem conexão com o provedor. Tente de novo." };
  }
  if (result && typeof result === "object" && "payment_token" in result) {
    return {
      ok: true,
      token: String((result as { payment_token: string }).payment_token),
      brand: card.brand,
    };
  }
  return rejected((result ?? {}) as EfiError);
}

async function tokenizeOnSimulator(provider: PaymentProvider, card: CheckedCard): Promise<TokenResult> {
  const data = toEfi(card);
  let response: Response;
  try {
    response = await fetch(new URL("/sim/card-token", provider.url), {
      method: "POST",
      headers: { "Content-Type": "application/json" },
      body: JSON.stringify({
        payee_code: provider.payeeCode,
        brand: data.brand,
        number: data.number,
        cvv: data.cvv,
        expiration_month: data.expirationMonth,
        expiration_year: data.expirationYear,
        holder_name: data.holderName,
        reuse: false,
      }),
    });
  } catch {
    return { ok: false, message: "O cartão não foi conferido: sem conexão com o provedor. Tente de novo." };
  }
  const body = (await response.json().catch(() => null)) as
    ({ data?: { payment_token?: string } } & EfiError) | null;
  if (response.ok && body?.data?.payment_token) {
    return { ok: true, token: body.data.payment_token, brand: card.brand };
  }
  if (response.status === 400) return rejected({ ...body, code: 400 });
  return { ok: false, message: UNAVAILABLE };
}
