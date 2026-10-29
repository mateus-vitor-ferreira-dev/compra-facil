"use client";

import {
  startTransition,
  useActionState,
  useEffect,
  useRef,
  useState,
  type FormEvent,
  type ReactNode,
} from "react";
import { Button } from "@/components/ui/button";
import { cn } from "@/lib/utils";
import { checkCard, type CardErrors, type CardField, type CardInput } from "@/lib/card";
import { tokenizeCard, type PaymentProvider } from "@/lib/card-token";
import {
  checkDelivery,
  DELIVERY_FIELDS,
  type DeliveryErrors,
  type DeliveryField,
  type DeliveryInput,
} from "@/lib/delivery";
import { formatMoney } from "@/lib/format";
import { ZIP_STORAGE_KEY, type ShippingItem, type ShippingOption } from "@/lib/shipping";
import { confirmOrder, type CheckoutState } from "./actions";
import { ShippingChoice, type ShippingQuoteState } from "./shipping-choice";
import { DeliveryFields } from "./delivery-fields";
import { CardFields, MethodChoice, type Method } from "./payment-fields";

const EMPTY_CARD: CardInput = { number: "", name: "", expiry: "", cvv: "" };
const FIELD_ORDER: CardField[] = ["number", "name", "expiry", "cvv"];

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/** A mais barata, quando o cliente ainda não escolheu (ou a escolhida não atende o CEP novo). */
const cheapest = (options: ShippingOption[]) => options[0]?.id;

type Props = {
  idempotencyKey: string;
  /** os produtos, sem o frete (com os descontos de campanha) */
  itemsTotalCents: number;
  /** os itens como o frete os cota (o SKU e a quantidade) */
  items: ShippingItem[];
  /** a cotação feita no servidor com o CEP do último pedido: sem JavaScript, o frete aparece igual */
  initialQuote?: { zip: string; options: ShippingOption[] };
  provider: PaymentProvider;
  /** a entrega para começar: a do último pedido ou, no primeiro, só o nome da conta */
  initialDelivery: DeliveryInput;
  remembered: boolean;
  /** os produtos e a conta, renderizados no servidor, antes da entrega e do pagamento */
  review: ReactNode;
  /** o resumo da conta, acima do botão */
  summary: ReactNode;
};

/**
 * O checkout inteiro é um formulário: a revisão, a entrega, a escolha do pagamento e o botão. A página
 * confere a entrega e, no cartão, os dados dele, troca o cartão por um token direto no provedor e só
 * então manda o pedido. Os campos do
 * cartão não têm `name`: nunca vão para a loja. A Idempotency-Key nasce com a página: o clique duplo, o
 * reenvio e a nova tentativa depois de um erro mandam a mesma chave, e o orders devolve o mesmo pedido.
 * Sem JavaScript, o PIX funciona do mesmo jeito.
 */
export function CheckoutForm({
  idempotencyKey,
  itemsTotalCents,
  items,
  initialQuote,
  provider,
  initialDelivery,
  remembered,
  review,
  summary,
}: Props) {
  const [state, action, pending] = useActionState<CheckoutState, FormData>(confirmOrder, {});
  // sem JavaScript, a página volta da action com o que foi digitado e os erros
  const [delivery, setDelivery] = useState(() => state.delivery?.values ?? initialDelivery);
  const [deliveryErrors, setDeliveryErrors] = useState<DeliveryErrors>(() => state.delivery?.errors ?? {});
  const deliveryFields = useRef<Partial<Record<DeliveryField, HTMLElement | null>>>({});
  const [quote, setQuote] = useState<ShippingQuoteState>(() =>
    initialQuote ? { kind: "done", ...initialQuote } : { kind: "waiting" },
  );
  const [service, setService] = useState<string | undefined>(
    () => initialQuote && cheapest(initialQuote.options),
  );
  const [shippingError, setShippingError] = useState<string>();
  const firstShipping = useRef<HTMLInputElement | null>(null);
  const chosen = quote.kind === "done" ? quote.options.find((o) => o.id === service) : undefined;
  const totalCents = itemsTotalCents + (chosen?.price_cents ?? 0);
  const total = brl(totalCents);

  // o frete acompanha o CEP da entrega: completo e diferente do cotado, cota de novo
  const zip = delivery.zip_code.replace(/\D/g, "");
  const quotedZip = quote.kind === "done" ? quote.zip : undefined;
  const itemsKey = JSON.stringify(items);
  useEffect(() => {
    if (zip.length !== 8 || zip === quotedZip) return;
    const timer = setTimeout(async () => {
      setQuote({ kind: "loading" });
      try {
        const response = await fetch("/api/frete", {
          method: "POST",
          headers: { "Content-Type": "application/json" },
          body: JSON.stringify({ zip_code: zip, items }),
        });
        if (!response.ok) {
          setQuote({
            kind: "error",
            message:
              response.status === 422
                ? "Não achamos frete para este CEP. Confira os dígitos."
                : "A cotação do frete não respondeu. Confira o CEP ou tente de novo em instantes.",
          });
          return;
        }
        const body = (await response.json()) as { zip_code: string; options: ShippingOption[] };
        localStorage.setItem(ZIP_STORAGE_KEY, zip);
        setQuote(
          body.options.length
            ? { kind: "done", zip: body.zip_code, options: body.options }
            : { kind: "error", message: "Nenhuma transportadora atende este CEP." },
        );
        setService((current) =>
          body.options.some((o) => o.id === current) ? current : cheapest(body.options),
        );
        setShippingError(undefined);
      } catch {
        setQuote({ kind: "error", message: "A cotação do frete não respondeu. Tente de novo em instantes." });
      }
    }, 300);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- recota pelo CEP e pelos itens
  }, [zip, quotedZip, itemsKey]);
  const [method, setMethod] = useState<Method>("pix");
  const [card, setCard] = useState(EMPTY_CARD);
  const [errors, setErrors] = useState<CardErrors>({});
  const [cardProblem, setCardProblem] = useState<string>();
  const [checking, setChecking] = useState(false);
  const fields = useRef<Partial<Record<CardField, HTMLInputElement | null>>>({});
  const busy = pending || checking;

  function changeCard(field: CardField, value: string) {
    setCard((c) => ({ ...c, [field]: value }));
    if (errors[field]) setErrors(({ [field]: _, ...rest }) => rest);
  }

  function changeDelivery(field: DeliveryField, value: string) {
    setDelivery((d) => ({ ...d, [field]: value }));
    if (deliveryErrors[field]) setDeliveryErrors(({ [field]: _, ...rest }) => rest);
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (busy) return;
    const data = new FormData(event.currentTarget);
    setCardProblem(undefined);
    // a entrega antes do cartão: o token do provedor é de uso único
    const wrong = checkDelivery(delivery);
    if (Object.keys(wrong).length > 0) {
      setDeliveryErrors(wrong);
      deliveryFields.current[DELIVERY_FIELDS.find((f) => wrong[f]) ?? "name"]?.focus();
      return;
    }
    // o frete antes do pagamento: sem ele, a loja não sabe o total nem o prazo
    if (!chosen) {
      setShippingError(
        quote.kind === "done"
          ? "Escolha o frete."
          : "Espere o frete do CEP ser calculado e escolha uma opção.",
      );
      if (firstShipping.current) firstShipping.current.focus();
      else document.getElementById("frete")?.scrollIntoView({ block: "center" });
      return;
    }
    if (method === "card") {
      const checked = checkCard(card);
      if (!checked.ok) {
        setErrors(checked.errors);
        fields.current[FIELD_ORDER.find((f) => checked.errors[f]) ?? "number"]?.focus();
        return;
      }
      setChecking(true);
      const result = await tokenizeCard(provider, checked.card);
      setChecking(false);
      if (!result.ok) {
        if (result.field) {
          setErrors({ [result.field]: result.message });
          fields.current[result.field]?.focus();
        } else {
          setCardProblem(result.message);
        }
        return;
      }
      data.set("card_token", result.token);
      data.set("card_brand", result.brand);
    }
    startTransition(() => action(data));
  }

  return (
    <form
      action={action}
      onSubmit={submit}
      noValidate
      className="mt-6 grid gap-10 lg:grid-cols-[minmax(0,1fr)_22rem]"
    >
      <input type="hidden" name="idempotency_key" value={idempotencyKey} />
      <div className="space-y-8">
        {review}
        <DeliveryFields
          delivery={delivery}
          errors={deliveryErrors}
          remembered={remembered}
          onChange={changeDelivery}
          onFill={(values) => {
            setDelivery((d) => ({ ...d, ...values }));
            setDeliveryErrors(({ street: _s, district: _d, city: _c, state: _u, ...rest }) => rest);
          }}
          register={(field, el) => {
            deliveryFields.current[field] = el;
          }}
        />
        <ShippingChoice
          quote={quote}
          selected={service}
          onChange={(id) => {
            setService(id);
            setShippingError(undefined);
          }}
          error={shippingError}
          register={(el) => {
            firstShipping.current = el;
          }}
        />
        <section aria-labelledby="pagamento" className="space-y-4">
          <h2 id="pagamento" className="text-xl font-semibold tracking-tight">
            Pagamento
          </h2>
          <MethodChoice method={method} onChange={setMethod} totalCents={totalCents} />
          {method === "card" && (
            <CardFields
              card={card}
              errors={errors}
              problem={cardProblem}
              totalCents={totalCents}
              disabled={busy}
              onChange={changeCard}
              onFill={(filled) => {
                setCard(filled);
                setErrors({});
              }}
              register={(field, el) => {
                fields.current[field] = el;
              }}
            />
          )}
          <p className="text-ink-soft max-w-prose text-sm">
            Esta loja é uma demonstração: o pagamento é simulado e nenhum dinheiro sai da sua conta.
          </p>
        </section>
      </div>

      <aside aria-labelledby="resumo" className="lg:sticky lg:top-6 lg:self-start">
        {summary}
        <dl className="mt-2 space-y-2">
          <div className="flex justify-between gap-4">
            <dt className="text-ink-soft">{chosen ? `Frete ${chosen.name.toLowerCase()}` : "Frete"}</dt>
            <dd className="tabular-nums">
              {chosen ? (chosen.price_cents === 0 ? "Grátis" : brl(chosen.price_cents)) : "a calcular"}
            </dd>
          </div>
          <div className="border-line flex items-end justify-between gap-4 border-t pt-3">
            <dt className="font-semibold">Total</dt>
            <dd className="font-condensed text-3xl leading-none font-extrabold">{total}</dd>
          </div>
        </dl>
        <p className="text-ink-soft mt-3 text-sm">
          O preço fica congelado no pedido. Se algum mudou desde a revisão, o pedido não é feito e o carrinho
          mostra o quê.
        </p>
        <div className="mt-6 space-y-3">
          {state.error && (
            <div role="alert" className="border-danger bg-surface space-y-1 rounded-md border-l-4 p-4">
              <p className="font-semibold">{state.error.title}</p>
              <p className="text-ink-soft text-sm">{state.error.message}</p>
              {state.error.traceId && (
                <p className="text-ink-soft pt-1 text-sm">
                  Se precisar do suporte, informe o código{" "}
                  <code className="text-ink bg-ink/8 rounded-sm px-1 text-xs break-all select-all">
                    {state.error.traceId}
                  </code>
                </p>
              )}
            </div>
          )}
          {/* aria-disabled, não disabled: o botão desligado perderia o foco no meio da conferência */}
          <Button
            type="submit"
            className={cn("w-full", busy && "cursor-progress opacity-50")}
            aria-disabled={busy || undefined}
          >
            {checking ? "Conferindo o cartão…" : pending ? "Fazendo o pedido…" : `Fazer o pedido de ${total}`}
          </Button>
        </div>
      </aside>
    </form>
  );
}
