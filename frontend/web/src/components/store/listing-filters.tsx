import { SlidersHorizontal, X } from "lucide-react";
import Link from "next/link";
import { listBrands } from "@/lib/catalog-api";
import { formatMoney } from "@/lib/format";
import {
  SORTS,
  activeFilterCount,
  withFilter,
  withoutRefinements,
  withoutValue,
  type Filters,
} from "@/lib/storefront";
import { FiltersForm, type FiltersFormProps } from "./filters-form";

type Params = Record<string, string | string[] | undefined>;

type Props = {
  path: string;
  params: Params;
  filters: Filters;
  /** parâmetros mantidos que não são filtro: a busca (q), o departamento das ofertas */
  keep: Record<string, string>;
  /** a categoria da listagem, para as marcas contarem só os produtos dela */
  category?: string;
  /** nas ofertas a promoção é fixa: some o filtro e a contagem */
  onSaleFixed?: boolean;
};

const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/**
 * Os filtros da listagem: numa coluna no desktop e, no celular, numa folha "Filtrar e ordenar" com a
 * contagem dos que estão ligados. Um <details> nativo: abre e fecha sem JavaScript e pelo teclado.
 */
export async function ListingFilters({ path, params, filters, keep, category, onSaleFixed = false }: Props) {
  const brands = await listBrands({
    q: filters.q,
    category,
    onSale: onSaleFixed || filters.onSale,
    minPriceCents: filters.minPriceCents,
    maxPriceCents: filters.maxPriceCents,
  });
  const count = activeFilterCount(filters, { onSaleFixed });
  const form: Omit<FiltersFormProps, "idPrefix" | "submitLabel" | "className"> = {
    action: path,
    keep,
    sorts: SORTS.filter((s) => s.value !== "relevancia" || filters.q).map(({ value, label }) => ({
      value,
      label,
    })),
    sort: filters.sort.value,
    brands: brands.ok ? brands.data : [],
    selectedBrands: filters.brands,
    minPrice: filters.minPriceCents,
    maxPrice: filters.maxPriceCents,
    onSale: onSaleFixed ? undefined : filters.onSale,
  };
  // remonta a cada mudança da URL: os campos não controlados acompanham o filtro tirado por uma ficha
  const key = new URLSearchParams(
    Object.entries(params).flatMap(([k, v]) => (Array.isArray(v) ? v : v ? [v] : []).map((x) => [k, x])),
  ).toString();

  return (
    <>
      <aside aria-labelledby="filtros-titulo" className="hidden lg:block">
        <h2 id="filtros-titulo" className="sr-only">
          Filtrar e ordenar
        </h2>
        <FiltersForm key={key} {...form} idPrefix="filtros" submitLabel="Aplicar filtros" />
      </aside>

      <details className="group border-line rounded-md border lg:hidden">
        <summary className="flex min-h-11 cursor-pointer list-none items-center gap-2 px-4 font-semibold [&::-webkit-details-marker]:hidden">
          <SlidersHorizontal aria-hidden="true" className="size-4" />
          Filtrar e ordenar
          {count > 0 && (
            <span className="bg-ink text-paper ml-auto grid min-w-6 place-items-center rounded-full px-1.5 text-sm tabular-nums">
              {count}
              <span className="sr-only"> {count === 1 ? "filtro ligado" : "filtros ligados"}</span>
            </span>
          )}
        </summary>
        <div className="border-line border-t px-4 pb-4">
          <FiltersForm key={key} {...form} idPrefix="folha" submitLabel="Ver resultados" className="pt-4" />
        </div>
      </details>
    </>
  );
}

/**
 * Os filtros ligados como fichas acima da grade: o que foi escolhido, à vista, e um toque para tirar
 * cada um ou todos.
 */
export function ActiveFilters({
  path,
  params,
  filters,
  onSaleFixed = false,
}: {
  path: string;
  params: Params;
  filters: Filters;
  onSaleFixed?: boolean;
}) {
  const { brands, minPriceCents: min, maxPriceCents: max } = filters;
  const chips: { label: string; href: string }[] = [
    ...brands.map((b) => ({ label: b, href: withoutValue(path, params, "marca", b) })),
  ];
  if (min !== undefined || max !== undefined) {
    const label =
      min !== undefined && max !== undefined
        ? `${brl(min)} a ${brl(max)}`
        : min !== undefined
          ? `A partir de ${brl(min)}`
          : `Até ${brl(max!)}`;
    chips.push({
      label,
      href: withFilter(path, { ...params, "preco-min": undefined }, "preco-max", null),
    });
  }
  if (filters.onSale && !onSaleFixed) {
    chips.push({ label: "Só em promoção", href: withFilter(path, params, "promocao", null) });
  }
  if (chips.length === 0) return null;

  return (
    <nav aria-label="Filtros ligados">
      <ul className="flex flex-wrap items-center gap-2">
        {chips.map((c) => (
          <li key={c.label}>
            <Link
              href={c.href}
              scroll={false}
              className="bg-ink/8 hover:bg-ink/15 inline-flex min-h-11 items-center gap-1.5 rounded-full pr-3 pl-4 text-sm font-medium sm:min-h-9"
            >
              {c.label}
              <X aria-hidden="true" className="size-4" />
              <span className="sr-only"> (tirar o filtro)</span>
            </Link>
          </li>
        ))}
        {chips.length > 1 && (
          <li>
            <Link
              href={withoutRefinements(path, params, onSaleFixed ? ["promocao"] : [])}
              scroll={false}
              className="inline-flex min-h-11 items-center px-2 text-sm font-semibold underline underline-offset-4 sm:min-h-9"
            >
              Limpar filtros
            </Link>
          </li>
        )}
      </ul>
    </nav>
  );
}
