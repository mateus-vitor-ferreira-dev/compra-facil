"use client";

import { useLayoutEffect, useRef, type ChangeEvent } from "react";
import { TextField } from "@/components/ui/field";
import {
  BRAND_NAMES,
  brandOf,
  caretAfterDigits,
  cvvLength,
  digitsOf,
  formatCardNumber,
  formatExpiry,
  installmentOptions,
  type CardErrors,
  type CardField,
  type CardInput,
} from "@/lib/card";
import { formatMoney } from "@/lib/format";

export type Method = "pix" | "card";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/** O cartão de teste do provedor: o nome do titular decide o resultado (APRO aprova; FUND, sem saldo). */
const TEST_CARD: CardInput = { number: "4509 9535 6623 3704", name: "APRO", expiry: "11/30", cvv: "123" };

/**
 * Como pagar. Cada opção diz o que acontece depois, que é o que muda para o cliente: o PIX é pago na
 * hora pelo app do banco; o cartão só é cobrado quando o fornecedor aceita o pedido.
 */
export function MethodChoice({
  method,
  onChange,
  totalCents,
}: {
  method: Method;
  onChange: (m: Method) => void;
  totalCents: number;
}) {
  const most = installmentOptions(totalCents).at(-1)?.count ?? 1;
  const options: { value: Method; title: string; detail: string }[] = [
    {
      value: "pix",
      title: "PIX",
      detail: "À vista, pelo app do banco. O código aparece no pedido e vale 30 minutos.",
    },
    {
      value: "card",
      title: "Cartão de crédito",
      detail: `${most > 1 ? `Em até ${most}× sem juros.` : "À vista."} A cobrança só acontece quando o fornecedor aceita o pedido.`,
    },
  ];
  return (
    <fieldset>
      <legend className="sr-only">Como pagar</legend>
      <div className="grid gap-3 sm:grid-cols-2">
        {options.map((o) => (
          <label
            key={o.value}
            className="border-line bg-surface has-checked:border-ink has-focus-visible:outline-focus relative flex cursor-pointer gap-3 rounded-md border-2 p-4 has-focus-visible:outline-2 has-focus-visible:outline-offset-2"
          >
            <input
              type="radio"
              name="method"
              value={o.value}
              checked={method === o.value}
              onChange={() => onChange(o.value)}
              className="border-ink/40 checked:border-ink mt-0.5 size-5 shrink-0 cursor-pointer appearance-none rounded-full border-2 outline-none checked:border-[6px]"
            />
            <span className="space-y-1">
              <span className="block font-semibold">{o.title}</span>
              <span className="text-ink-soft block text-sm">{o.detail}</span>
            </span>
          </label>
        ))}
      </div>
    </fieldset>
  );
}

type CardFieldsProps = {
  card: CardInput;
  errors: CardErrors;
  /** o que deu errado com o provedor, sem ser de um campo (sem conexão, fora do ar) */
  problem?: string;
  totalCents: number;
  disabled: boolean;
  onChange: (field: CardField, value: string) => void;
  onFill: (card: CardInput) => void;
  register: (field: CardField, el: HTMLInputElement | null) => void;
};

/**
 * Os dados do cartão, na ordem em que estão impressos nele. Os campos não têm `name` (não vão no
 * formulário) e usam o autocomplete `cc-*`, para o navegador preencher o cartão salvo.
 */
export function CardFields({
  card,
  errors,
  problem,
  totalCents,
  disabled,
  onChange,
  onFill,
  register,
}: CardFieldsProps) {
  const brand = brandOf(digitsOf(card.number));
  const caret = useRef<{ el: HTMLInputElement; at: number } | null>(null);

  // reformatar ("4509 9535…") joga o cursor para o fim: ele volta para depois do mesmo dígito
  useLayoutEffect(() => {
    if (!caret.current) return;
    const { el, at } = caret.current;
    caret.current = null;
    if (document.activeElement === el) el.setSelectionRange(at, at);
  }, [card.number, card.expiry]);

  function reformat(
    e: ChangeEvent<HTMLInputElement>,
    field: "number" | "expiry",
    format: (v: string) => string,
  ) {
    const el = e.target;
    const before = digitsOf(el.value.slice(0, el.selectionStart ?? el.value.length)).length;
    const next = format(el.value);
    caret.current = { el, at: caretAfterDigits(next, before) };
    onChange(field, next);
  }

  return (
    <div className="border-ink/15 space-y-4 border-l-2 pl-4 sm:pl-5">
      {problem && (
        <p role="alert" className="border-danger bg-surface rounded-md border-l-4 p-3 text-sm">
          {problem}
        </p>
      )}
      <div className="relative">
        <TextField
          label="Número do cartão"
          ref={(el) => register("number", el)}
          inputMode="numeric"
          autoComplete="cc-number"
          value={card.number}
          onChange={(e) => reformat(e, "number", formatCardNumber)}
          error={errors.number}
          disabled={disabled}
          aria-describedby={brand ? "cartao-bandeira" : undefined}
          className="[&_input]:pr-36"
        />
        {brand && (
          <span
            id="cartao-bandeira"
            className="text-ink-soft pointer-events-none absolute top-[1.875rem] right-3 flex h-11 items-center text-sm font-semibold"
          >
            {BRAND_NAMES[brand]}
          </span>
        )}
      </div>
      <TextField
        label="Nome impresso no cartão"
        ref={(el) => register("name", el)}
        autoComplete="cc-name"
        autoCapitalize="characters"
        spellCheck={false}
        value={card.name}
        onChange={(e) => onChange("name", e.target.value)}
        error={errors.name}
        disabled={disabled}
      />
      <div className="grid grid-cols-2 gap-4">
        <TextField
          label="Validade"
          hint="MM/AA"
          ref={(el) => register("expiry", el)}
          inputMode="numeric"
          autoComplete="cc-exp"
          value={card.expiry}
          onChange={(e) => reformat(e, "expiry", formatExpiry)}
          error={errors.expiry}
          disabled={disabled}
        />
        <TextField
          label="Código de segurança"
          hint={brand === "amex" ? "4 dígitos, na frente" : "3 dígitos, no verso"}
          ref={(el) => register("cvv", el)}
          inputMode="numeric"
          autoComplete="cc-csc"
          maxLength={cvvLength(brand)}
          value={card.cvv}
          onChange={(e) => onChange("cvv", digitsOf(e.target.value))}
          error={errors.cvv}
          disabled={disabled}
        />
      </div>
      <div className="space-y-1.5">
        <label htmlFor="parcelas" className="block font-medium">
          Parcelas
        </label>
        <select
          id="parcelas"
          name="installments"
          disabled={disabled}
          className="border-ink/25 bg-surface text-ink h-11 w-full cursor-pointer rounded-md border px-3 text-base outline-none focus-visible:outline-2"
        >
          {installmentOptions(totalCents).map(({ count, eachCents }) => (
            <option key={count} value={count}>
              {count === 1 ? `À vista, ${brl(eachCents)}` : `${count}× de ${brl(eachCents)} sem juros`}
            </option>
          ))}
        </select>
      </div>
      <div className="text-ink-soft flex flex-wrap items-center gap-x-3 gap-y-1 text-sm">
        <p>No cartão de teste, o nome APRO aprova e FUND recusa por falta de saldo.</p>
        <button
          type="button"
          onClick={() => onFill(TEST_CARD)}
          disabled={disabled}
          className="text-ink inline-flex min-h-11 cursor-pointer items-center font-semibold underline underline-offset-4 sm:min-h-8"
        >
          Usar o cartão de teste
        </button>
      </div>
    </div>
  );
}
