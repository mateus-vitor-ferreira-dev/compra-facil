"use client";

import Form from "next/form";
import { useState, type FormEvent } from "react";
import { Button } from "@/components/ui/button";
import type { BrandCount, CategoryCount } from "@/lib/catalog-api";
import { normalize } from "@/lib/storefront";
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
  /** a faceta das categorias filhas (tipos de uma subcategoria, #212) */
  children?: { label: string; options: CategoryCount[]; selected: string[] };
  minPrice?: number;
  maxPrice?: number;
  /** "Só em promoção" (não aparece nas ofertas, que já são só promoção) */
  onSale?: boolean;
  submitLabel: string;
  /** na folha do celular o botão fica preso no rodapé, com o total ("Ver 128 produtos") */
  stickySubmit?: boolean;
  className?: string;
};

/** marcas à vista; as outras ficam em "Ver todas" (Baymard: valores demais escondem os úteis) */
const VISIBLE_BRANDS = 8;
/** a partir daqui, a lista completa ganha uma busca */
const SEARCHABLE_BRANDS = 15;

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
  children,
  minPrice,
  maxPrice,
  onSale,
  submitLabel,
  stickySubmit = false,
  className,
}: FiltersFormProps) {
  const [brandSearch, setBrandSearch] = useState("");
  const id = (name: string) => `${idPrefix}-${name}`;
  // as marcas escolhidas continuam na lista mesmo sem produto nos outros filtros: dá para desmarcar
  const options = [
    ...brands,
    ...selectedBrands
      .filter((b) => !brands.some((x) => x.name.toLowerCase() === b.toLowerCase()))
      .map((name) => ({ name, count: 0 })),
  ];
  const chosen = new Set(selectedBrands.map((b) => b.toLowerCase()));
  // as marcas marcadas sobem para as visíveis: o que está ligado nunca fica escondido em "Ver todas"
  const ordered = [
    ...options.filter((b) => chosen.has(b.name.toLowerCase())),
    ...options.filter((b) => !chosen.has(b.name.toLowerCase())),
  ];
  const visible = ordered.slice(0, VISIBLE_BRANDS);
  const hidden = ordered.slice(VISIBLE_BRANDS);
  const typeOptions = children
    ? [
        ...children.options,
        // tipo marcado sem produto nos outros filtros continua na lista, para desmarcar
        ...children.selected
          .filter((slug) => !children.options.some((o) => o.slug === slug))
          .map((slug) => ({ slug, name: slug, count: 0 })),
      ]
    : [];
  const chosenTypes = new Set(children?.selected ?? []);

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
  const choice = choiceClass;
  const input = inputClass;

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

      {children && typeOptions.length > 1 && (
        <div role="group" aria-labelledby={id("tipo")} className="space-y-1 py-5">
          <p id={id("tipo")} className={cn(legend, "mb-2")}>
            {children.label}
          </p>
          {typeOptions.map((t) => (
            <label key={t.slug} className={choice}>
              <input
                type="checkbox"
                name="tipo"
                value={t.slug}
                defaultChecked={chosenTypes.has(t.slug)}
                className={input}
              />
              <span className="min-w-0 flex-1 truncate">{t.name}</span>
              <Count n={t.count} />
            </label>
          ))}
        </div>
      )}

      {options.length > 0 && (
        <div role="group" aria-labelledby={id("marca")} className="space-y-1 py-5">
          <p id={id("marca")} className={cn(legend, "mb-2")}>
            Marca
          </p>
          {visible.map((b) => (
            <BrandOption key={b.name} brand={b} checked={chosen.has(b.name.toLowerCase())} />
          ))}
          {hidden.length > 0 && (
            <details className="group/marcas">
              <summary className="text-ink flex min-h-11 cursor-pointer list-none items-center px-2 text-sm font-semibold underline underline-offset-4 sm:min-h-9 [&::-webkit-details-marker]:hidden">
                <span className="group-open/marcas:hidden">Ver todas as {options.length} marcas</span>
                <span className="hidden group-open/marcas:inline">Ver menos marcas</span>
              </summary>
              {options.length > SEARCHABLE_BRANDS && (
                <div className="px-2 pt-1 pb-2">
                  <label htmlFor={id("busca-marca")} className="sr-only">
                    Buscar marca
                  </label>
                  <input
                    id={id("busca-marca")}
                    type="search"
                    // sem name: não vai para a URL
                    value={brandSearch}
                    onChange={(e) => setBrandSearch(e.target.value)}
                    placeholder="Buscar marca"
                    autoComplete="off"
                    className="border-field-line bg-surface focus:border-ink h-10 w-full rounded-md border px-3 text-sm outline-none"
                  />
                </div>
              )}
              {hidden
                .filter(
                  (b) => !brandSearch.trim() || normalize(b.name).includes(normalize(brandSearch.trim())),
                )
                .map((b) => (
                  <BrandOption key={b.name} brand={b} checked={chosen.has(b.name.toLowerCase())} />
                ))}
            </details>
          )}
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
              <div className="border-field-line bg-surface focus-within:border-ink flex h-11 items-center gap-1 rounded-md border px-3">
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

      <div
        className={cn(
          "pt-5",
          // na folha: preso no rodapé da tela enquanto rola a lista de filtros
          stickySubmit && "bg-paper sticky bottom-0 -mx-4 px-4 pb-4 shadow-[0_-1px_0_var(--color-line)]",
        )}
      >
        <Button type="submit" variant={stickySubmit ? "primary" : "secondary"} className="w-full">
          {submitLabel}
        </Button>
      </div>
    </Form>
  );
}

const choiceClass =
  "flex min-h-11 cursor-pointer items-center gap-3 rounded-md px-2 hover:bg-ink/5 sm:min-h-9 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-focus";
const inputClass = "accent-ink size-4 shrink-0 cursor-pointer focus-visible:outline-none";

function Count({ n }: { n: number }) {
  return (
    <span className="text-ink-soft text-sm tabular-nums">
      {n > 0 ? n : "–"}
      <span className="sr-only"> {n === 1 ? "produto" : "produtos"}</span>
    </span>
  );
}

function BrandOption({ brand, checked }: { brand: BrandCount; checked: boolean }) {
  return (
    <label className={choiceClass}>
      <input
        type="checkbox"
        name="marca"
        value={brand.name}
        defaultChecked={checked}
        className={inputClass}
      />
      <span className="min-w-0 flex-1 truncate">{brand.name}</span>
      <Count n={brand.count} />
    </label>
  );
}
