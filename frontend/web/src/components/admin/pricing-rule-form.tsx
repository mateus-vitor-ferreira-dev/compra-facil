"use client";

import { useActionState, useId, useState } from "react";
import { savePricingRule } from "@/app/admin/fornecedor/actions";
import type { FormState } from "@/app/admin/produtos/actions";
import { TextField } from "@/components/ui/field";
import { SubmitButton } from "@/components/ui/submit-button";
import { formatMoney } from "@/lib/format";
import { parseReais } from "@/lib/money-input";
import { salePrice } from "@/lib/pricing";
import { FormError } from "./form-error";

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });
const usd = (cents: number) =>
  new Intl.NumberFormat("pt-BR", { style: "currency", currency: "USD" }).format(cents / 100);
const rate = (milli: number) =>
  (milli / 1000).toLocaleString("pt-BR", { minimumFractionDigits: 2, maximumFractionDigits: 3 });

type Category = { id: string; name: string };

/**
 * Uma regra de margem: a padrão (sem categories) ou a de uma categoria. Embaixo, a conta de um custo de
 * exemplo, refeita a cada tecla: a equipe vê o preço que a regra dá antes de gravar.
 */
export function PricingRuleForm({
  rateMilli,
  initial,
  categories,
  submitLabel,
}: {
  rateMilli: number;
  initial: { margin_percent: number; min_price_cents: number };
  categories?: { id: string; name: string; children: Category[] }[];
  submitLabel: string;
}) {
  const [state, action] = useActionState<FormState, FormData>(savePricingRule, {});
  const v = state.values ?? {};
  const err = state.fields ?? {};
  const [margin, setMargin] = useState(v.margin_percent ?? String(initial.margin_percent));
  const [min, setMin] = useState(v.min_price ?? (initial.min_price_cents / 100).toFixed(2).replace(".", ","));
  const [cost, setCost] = useState("12,40");
  const selectId = useId();

  const costCents = parseReais(cost);
  const minCents = min === "" ? 0 : parseReais(min);
  const marginN = /^\d+$/.test(margin) ? Number(margin) : null;
  const sample =
    costCents && costCents > 0 && marginN !== null && minCents !== null
      ? salePrice(costCents, rateMilli, marginN, minCents)
      : null;

  return (
    <form action={action} className="space-y-4" noValidate>
      <FormError error={state.error} />
      {categories && (
        <div className="space-y-1.5">
          <label htmlFor={selectId} className="block font-medium">
            Categoria
          </label>
          <select
            id={selectId}
            name="category_id"
            defaultValue={v.category_id ?? ""}
            aria-invalid={err.category_id ? true : undefined}
            aria-describedby={err.category_id ? `${selectId}-erro` : `${selectId}-dica`}
            className="bg-surface border-field-line aria-invalid:border-danger h-11 w-full rounded-md border px-3"
          >
            <option value="">Escolha…</option>
            {categories.map((root) => (
              <optgroup key={root.id} label={root.name}>
                <option value={root.id}>{root.name} (o departamento todo)</option>
                {root.children.map((c) => (
                  <option key={c.id} value={c.id}>
                    {c.name}
                  </option>
                ))}
              </optgroup>
            ))}
          </select>
          {err.category_id ? (
            <p id={`${selectId}-erro`} className="text-danger text-sm font-medium">
              {err.category_id}
            </p>
          ) : (
            <p id={`${selectId}-dica`} className="text-ink-soft text-sm">
              Vale também para o que está dentro dela, até uma regra mais específica.
            </p>
          )}
        </div>
      )}
      <div className="grid gap-4 sm:grid-cols-2">
        <TextField
          label="Margem (%)"
          name="margin_percent"
          inputMode="numeric"
          value={margin}
          onChange={(e) => setMargin(e.target.value)}
          error={err.margin_percent}
          hint="Sobre o custo já em reais."
          required
        />
        <TextField
          label="Preço mínimo (R$)"
          name="min_price"
          inputMode="decimal"
          value={min}
          onChange={(e) => setMin(e.target.value)}
          error={err.min_price}
          hint="Nenhum produto sai por menos."
        />
      </div>

      <div className="bg-ink/4 space-y-3 rounded-md p-4">
        <TextField
          label="Custo de exemplo (US$)"
          inputMode="decimal"
          value={cost}
          onChange={(e) => setCost(e.target.value)}
          hint="Só para a conta abaixo; não é gravado."
          className="max-w-48"
        />
        {sample ? (
          <ol aria-label="A conta do preço" className="space-y-1 text-sm tabular-nums">
            <li className="flex justify-between gap-4">
              <span>
                {usd(costCents!)} × {rate(rateMilli)}
              </span>
              <span>{brl(sample.costBrl)}</span>
            </li>
            <li className="flex justify-between gap-4">
              <span>+ {marginN}% de margem</span>
              <span>{brl(sample.withMargin)}</span>
            </li>
            {sample.minApplied && (
              <li className="flex justify-between gap-4">
                <span>abaixo do mínimo: vale o mínimo</span>
                <span>{brl(minCents!)}</span>
              </li>
            )}
            <li className="border-ink flex items-baseline justify-between gap-4 border-t-2 pt-2">
              <span className="font-semibold">Preço na loja, terminando em ,90</span>
              <span className="font-condensed text-2xl leading-none font-extrabold">{brl(sample.price)}</span>
            </li>
          </ol>
        ) : null}
        {/* um anúncio só, com contexto: a conta inteira relida a cada tecla seria ruído */}
        <p aria-live="polite" aria-atomic="true" className="sr-only">
          {sample ? `Preço na loja: ${brl(sample.price)}` : ""}
        </p>
        {sample ? null : (
          <p className="text-ink-soft text-sm">Preencha a margem, o mínimo e um custo para ver a conta.</p>
        )}
      </div>

      <SubmitButton pendingLabel="Gravando…">{submitLabel}</SubmitButton>
    </form>
  );
}
