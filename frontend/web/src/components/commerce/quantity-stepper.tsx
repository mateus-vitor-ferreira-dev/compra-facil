"use client";

import { Minus, Plus } from "lucide-react";
import { useState } from "react";
import { MAX_QUANTITY } from "@/lib/cart-rules";
import { cn } from "@/lib/utils";

/**
 * Quantidade com − e + (44 px no toque) e o número digitável no meio. Vai no formulário como
 * `quantity`; o máximo é o estoque disponível ou 99.
 */
export function QuantityStepper({
  max = MAX_QUANTITY,
  defaultValue = 1,
  id,
  className,
}: {
  max?: number;
  defaultValue?: number;
  id: string;
  className?: string;
}) {
  const limit = Math.max(1, Math.min(max, MAX_QUANTITY));
  const [value, setValue] = useState(Math.min(defaultValue, limit));
  const step =
    "text-ink hover:bg-ink/8 grid size-11 cursor-pointer place-items-center aria-disabled:cursor-not-allowed aria-disabled:opacity-40 aria-disabled:hover:bg-transparent";

  return (
    <div className={cn("flex flex-col gap-1", className)}>
      <label htmlFor={id} className="text-ink-soft text-sm">
        Quantidade
      </label>
      {/* o campo não tem contorno próprio (ficaria entre os botões): o foco aparece no grupo inteiro */}
      <div className="border-field-line bg-surface has-[input:focus-visible]:outline-focus inline-flex items-center rounded-md border has-[input:focus-visible]:outline-2 has-[input:focus-visible]:outline-offset-2">
        <button
          type="button"
          className={step}
          onClick={() => setValue((v) => Math.max(1, v - 1))}
          // aria-disabled, e não disabled: o botão desligado derrubaria o foco de quem usa teclado (#273)
          aria-disabled={value <= 1 || undefined}
          aria-label="Diminuir a quantidade"
          aria-controls={id}
        >
          <Minus aria-hidden="true" className="size-4" />
        </button>
        <input
          id={id}
          name="quantity"
          type="number"
          inputMode="numeric"
          min={1}
          max={limit}
          required
          value={value}
          onChange={(e) => setValue(Number(e.target.value))}
          onBlur={() => setValue((v) => Math.max(1, Math.min(Math.floor(v) || 1, limit)))}
          className="font-condensed h-11 w-12 [appearance:textfield] bg-transparent text-center text-lg font-bold outline-none [&::-webkit-inner-spin-button]:appearance-none [&::-webkit-outer-spin-button]:appearance-none"
        />
        <button
          type="button"
          className={step}
          onClick={() => setValue((v) => Math.min(limit, v + 1))}
          aria-disabled={value >= limit || undefined}
          aria-label="Aumentar a quantidade"
          aria-controls={id}
        >
          <Plus aria-hidden="true" className="size-4" />
        </button>
      </div>
    </div>
  );
}
