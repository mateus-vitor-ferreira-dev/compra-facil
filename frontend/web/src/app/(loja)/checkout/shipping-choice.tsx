"use client";

import { formatMoney } from "@/lib/format";
import { arrival, businessDays, type ShippingOption } from "@/lib/shipping";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

export type ShippingQuoteState =
  | { kind: "waiting" } // sem CEP completo ainda
  | { kind: "loading" }
  | { kind: "done"; zip: string; options: ShippingOption[] }
  | { kind: "error"; message: string };

/**
 * O frete do pedido (#222), cotado pelo CEP da entrega: cada opção diz quanto custa e em que datas chega,
 * antes do pagamento (Decreto 7.962). O preço e o prazo o orders recota no servidor; daqui vai só o serviço.
 */
export function ShippingChoice({
  quote,
  selected,
  onChange,
  error,
  register,
}: {
  quote: ShippingQuoteState;
  selected?: string;
  onChange: (service: string) => void;
  error?: string;
  register: (el: HTMLInputElement | null) => void;
}) {
  return (
    <section aria-labelledby="frete" className="space-y-4">
      <h2 id="frete" className="text-xl font-semibold tracking-tight">
        Frete
      </h2>
      <div aria-live="polite" aria-busy={quote.kind === "loading" || undefined}>
        {quote.kind === "waiting" && (
          <p className="text-ink-soft">Digite o CEP da entrega para ver o frete e o prazo.</p>
        )}
        {quote.kind === "loading" && <p className="text-ink-soft">Calculando o frete para o CEP…</p>}
        {quote.kind === "error" && <p className="text-danger font-medium">{quote.message}</p>}
        {quote.kind === "done" && (
          <fieldset aria-describedby={error ? "frete-erro" : undefined}>
            <legend className="sr-only">Escolha o frete</legend>
            <div className="grid gap-3 sm:grid-cols-2">
              {quote.options.map((o, i) => (
                <label
                  key={o.id}
                  className="border-line bg-surface has-checked:border-ink has-focus-visible:outline-focus relative flex cursor-pointer gap-3 rounded-md border-2 p-4 has-focus-visible:outline-2 has-focus-visible:outline-offset-2"
                >
                  <input
                    ref={i === 0 ? register : undefined}
                    type="radio"
                    name="shipping_service"
                    value={o.id}
                    checked={selected === o.id}
                    onChange={() => onChange(o.id)}
                    className="border-ink/40 checked:border-ink mt-0.5 size-5 shrink-0 cursor-pointer appearance-none rounded-full border-2 outline-none checked:border-[6px]"
                  />
                  <span className="flex min-w-0 flex-1 items-baseline justify-between gap-3">
                    <span className="space-y-1">
                      <span className="block font-semibold">{o.name}</span>
                      <span className="text-ink-soft block text-sm">
                        {arrival(o)}
                        <span className="sr-only"> ({businessDays(o)})</span>
                      </span>
                    </span>
                    <span className="font-condensed text-lg font-extrabold whitespace-nowrap tabular-nums">
                      {o.price_cents === 0 ? "Grátis" : brl(o.price_cents)}
                    </span>
                  </span>
                </label>
              ))}
            </div>
          </fieldset>
        )}
        {error && (
          <p id="frete-erro" className="text-danger mt-2 text-sm font-medium">
            {error}
          </p>
        )}
      </div>
    </section>
  );
}
