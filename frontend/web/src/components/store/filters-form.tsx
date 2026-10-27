"use client";

import Form from "next/form";
import type { FormEvent } from "react";
import { Button } from "@/components/ui/button";
import type { BrandCount } from "@/lib/catalog-api";
import { cn } from "@/lib/utils";

type Option = { value: string; label: string };

export type FiltersFormProps = {
  /** a própria listagem: o formulário é GET para ela */
  action: string;
  /** ids únicos por instância: o formulário aparece na coluna (desktop) e na folha (celular) */
  idPrefix: string;
  /** parâmetros que a listagem mantém e não são filtro (a busca, o departamento das ofertas) */
  keep: Record<string, string>;
  sorts: Option[];
  sort: string;
  brands: BrandCount[];
  selectedBrands: string[];
  minPrice?: number;
  maxPrice?: number;
  /** "Só em promoção" (não aparece nas ofertas, que já são só promoção) */
  onSale?: boolean;
  submitLabel: string;
  className?: string;
};

/**
 * Ordem, marca, faixa de preço e promoção num formulário GET para a própria listagem: funciona sem
 * JavaScript (o botão aplica) e, com ele, a ordem e as caixas aplicam na hora, sem recarregar a página.
 */
export function FiltersForm({
  action,
  idPrefix,
  keep,
  sorts,
  sort,
  brands,
  selectedBrands,
  minPrice,
  maxPrice,
  onSale,
  submitLabel,
  className,
}: FiltersFormProps) {
  const id = (name: string) => `${idPrefix}-${name}`;
  // as marcas escolhidas continuam na lista mesmo sem produto nos outros filtros: dá para desmarcar
  const options = [
    ...brands,
    ...selectedBrands
      .filter((b) => !brands.some((x) => x.name.toLowerCase() === b.toLowerCase()))
      .map((name) => ({ name, count: 0 })),
  ];
  const chosen = new Set(selectedBrands.map((b) => b.toLowerCase()));

  // campos vazios não vão para a URL (?preco-min=&preco-max=)
  function dropEmpty(event: FormEvent<HTMLFormElement>) {
    for (const el of Array.from(event.currentTarget.elements)) {
      if (el instanceof HTMLInputElement && el.type === "text" && !el.value.trim()) el.disabled = true;
    }
    setTimeout(() => {
      for (const el of Array.from((event.target as HTMLFormElement).elements)) {
        if (el instanceof HTMLInputElement) el.disabled = false;
      }
    });
  }

  function applyNow(event: FormEvent<HTMLFormElement>) {
    const target = event.target as HTMLInputElement;
    if (target.type === "radio" || target.type === "checkbox") event.currentTarget.requestSubmit();
  }

  const legend = "text-base font-semibold";
  const choice =
    "flex min-h-11 cursor-pointer items-center gap-3 rounded-md px-2 hover:bg-ink/5 sm:min-h-9 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-focus";
  const input = "accent-ink size-4 shrink-0 cursor-pointer focus-visible:outline-none";

  return (
    <Form
      action={action}
      scroll={false}
      onSubmit={dropEmpty}
      onChange={applyNow}
      className={cn("divide-line divide-y", className)}
    >
      {Object.entries(keep).map(([name, value]) => (
        <input key={name} type="hidden" name={name} value={value} />
      ))}

      <div role="radiogroup" aria-labelledby={id("ordem")} className="space-y-1 pb-5">
        <p id={id("ordem")} className={cn(legend, "mb-2")}>
          Ordenar por
        </p>
        {sorts.map((s) => (
          <label key={s.value} className={choice}>
            <input
              type="radio"
              name="ordem"
              value={s.value}
              defaultChecked={s.value === sort}
              className={input}
            />
            {s.label}
          </label>
        ))}
      </div>

      {onSale !== undefined && (
        <div className="py-5">
          <label className={cn(choice, "font-medium")}>
            <input type="checkbox" name="promocao" value="1" defaultChecked={onSale} className={input} />
            Só em promoção
          </label>
        </div>
      )}

      {options.length > 0 && (
        <div role="group" aria-labelledby={id("marca")} className="space-y-1 py-5">
          <p id={id("marca")} className={cn(legend, "mb-2")}>
            Marca
          </p>
          {options.map((b) => (
            <label key={b.name} className={choice}>
              <input
                type="checkbox"
                name="marca"
                value={b.name}
                defaultChecked={chosen.has(b.name.toLowerCase())}
                className={input}
              />
              <span className="min-w-0 flex-1 truncate">{b.name}</span>
              <span className="text-ink-soft text-sm tabular-nums">
                {b.count > 0 ? b.count : "–"}
                <span className="sr-only"> {b.count === 1 ? "produto" : "produtos"}</span>
              </span>
            </label>
          ))}
        </div>
      )}

      <div role="group" aria-labelledby={id("preco")} className="py-5">
        <p id={id("preco")} className={cn(legend, "mb-2")}>
          Preço
        </p>
        <div className="grid grid-cols-2 gap-3">
          {(
            [
              ["preco-min", "De", minPrice],
              ["preco-max", "Até", maxPrice],
            ] as const
          ).map(([name, label, value]) => (
            <div key={name} className="space-y-1">
              <label htmlFor={id(name)} className="text-ink-soft text-sm">
                {label}
              </label>
              <div className="border-ink/25 bg-surface focus-within:border-ink flex h-11 items-center gap-1 rounded-md border px-3">
                <span aria-hidden="true" className="text-ink-soft text-sm">
                  R$
                </span>
                <input
                  id={id(name)}
                  name={name}
                  type="text"
                  inputMode="numeric"
                  autoComplete="off"
                  pattern="[0-9.,]*"
                  defaultValue={value !== undefined ? String(Math.round(value / 100)) : ""}
                  aria-label={`${label}, em reais`}
                  className="w-full min-w-0 bg-transparent tabular-nums outline-none"
                />
              </div>
            </div>
          ))}
        </div>
      </div>

      <div className="pt-5">
        <Button type="submit" variant="secondary" className="w-full">
          {submitLabel}
        </Button>
      </div>
    </Form>
  );
}
