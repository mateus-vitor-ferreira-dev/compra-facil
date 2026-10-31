"use client";

import { useEffect, useId, useState, useSyncExternalStore, type FormEvent } from "react";
import { formatZipCode } from "@/lib/delivery";
import { formatMoney } from "@/lib/format";
import {
  arrival,
  businessDays,
  ZIP_STORAGE_KEY,
  type ShippingItem,
  type ShippingOption,
} from "@/lib/shipping";
import { cn } from "@/lib/utils";

type State =
  | { kind: "idle" }
  | { kind: "loading" }
  | { kind: "done"; zip: string; options: ShippingOption[] }
  | { kind: "error"; message: string };

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

// o CEP guardado no navegador; nulo no servidor (a página sai igual para todos)
const noop = () => () => {};
const savedZip = () => localStorage.getItem(ZIP_STORAGE_KEY);

/**
 * Frete e prazo por CEP (#222), na página do produto e no carrinho: o cliente vê quanto custa e quando
 * chega antes de pagar (Decreto 7.962). O CEP fica guardado no navegador, para o carrinho e o checkout
 * cotarem sem redigitar. Sem JavaScript, o campo não aparece: o checkout mostra o frete do mesmo jeito.
 */
export function ShippingEstimate({ items, className }: { items: ShippingItem[]; className?: string }) {
  const id = useId();
  const saved = useSyncExternalStore(noop, savedZip, () => null);
  const [typed, setTyped] = useState<string>();
  const zip = typed ?? (saved ? formatZipCode(saved) : "");
  const [state, setState] = useState<State>({ kind: "idle" });
  const key = JSON.stringify(items);

  async function quote(value: string) {
    const digits = value.replace(/\D/g, "");
    if (digits.length !== 8) {
      setState({ kind: "error", message: "Digite os 8 dígitos do CEP." });
      return;
    }
    setState({ kind: "loading" });
    try {
      const response = await fetch("/api/frete", {
        method: "POST",
        headers: { "Content-Type": "application/json" },
        body: JSON.stringify({ zip_code: digits, items }),
      });
      if (response.status === 422)
        return setState({ kind: "error", message: "Confira o CEP: não achamos frete para ele." });
      if (!response.ok)
        return setState({
          kind: "error",
          message: "A cotação do frete não respondeu. Tente de novo em instantes.",
        });
      const body = (await response.json()) as { zip_code: string; options: ShippingOption[] };
      localStorage.setItem(ZIP_STORAGE_KEY, digits);
      setState(
        body.options.length
          ? { kind: "done", zip: body.zip_code, options: body.options }
          : { kind: "error", message: "Nenhuma transportadora atende este CEP." },
      );
    } catch {
      setState({ kind: "error", message: "A cotação do frete não respondeu. Tente de novo em instantes." });
    }
  }

  // o CEP de antes: cota sozinho, e de novo quando os itens mudam (outra quantidade no carrinho)
  useEffect(() => {
    if (!saved) return;
    const timer = setTimeout(() => void quote(saved), 0);
    return () => clearTimeout(timer);
    // eslint-disable-next-line react-hooks/exhaustive-deps -- a cotação depende só do CEP guardado e dos itens
  }, [saved, key]);

  function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    void quote(zip);
  }

  return (
    <section aria-labelledby={`${id}-titulo`} className={cn("space-y-3", className)}>
      <h2 id={`${id}-titulo`} className="font-semibold">
        Frete e prazo
      </h2>
      <form onSubmit={submit} className="space-y-1.5">
        <div className="flex items-baseline justify-between gap-3">
          <label htmlFor={`${id}-cep`} className="text-sm font-medium">
            CEP
          </label>
          <a
            href="https://buscacepinter.correios.com.br/app/endereco/index.php"
            target="_blank"
            rel="noreferrer"
            className="text-ink-soft inline-flex min-h-11 items-center text-sm underline underline-offset-4 sm:min-h-6"
          >
            Não sei o CEP<span className="sr-only"> (busca dos Correios, abre em outra aba)</span>
          </a>
        </div>
        <div className="flex gap-2">
          <input
            id={`${id}-cep`}
            inputMode="numeric"
            autoComplete="postal-code"
            value={zip}
            onChange={(e) => setTyped(formatZipCode(e.target.value))}
            aria-describedby={`${id}-resultado`}
            className="border-field-line bg-surface text-ink h-11 w-36 rounded-md border px-3 text-base tabular-nums outline-none focus-visible:outline-2"
          />
          <button
            type="submit"
            disabled={state.kind === "loading"}
            className="border-field-line bg-surface text-ink hover:border-ink/60 h-11 cursor-pointer rounded-md border px-4 font-semibold disabled:opacity-50"
          >
            {state.kind === "loading" ? "Calculando…" : "Calcular"}
          </button>
        </div>
      </form>

      <div id={`${id}-resultado`} aria-live="polite">
        {state.kind === "error" && <p className="text-danger text-sm font-medium">{state.message}</p>}
        {state.kind === "done" && (
          <ul className="divide-line border-line divide-y border-y">
            {state.options.map((o) => (
              <li key={o.id} className="flex items-baseline justify-between gap-4 py-2.5">
                <div>
                  <p className="font-medium">{o.name}</p>
                  <p className="text-ink-soft text-sm">
                    {arrival(o)} <span className="sr-only">({businessDays(o)})</span>
                  </p>
                </div>
                <p className="font-condensed text-lg font-extrabold tabular-nums">
                  {o.price_cents === 0 ? "Grátis" : brl(o.price_cents)}
                </p>
              </li>
            ))}
          </ul>
        )}
      </div>
    </section>
  );
}
