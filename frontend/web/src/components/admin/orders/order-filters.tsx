import { Search } from "lucide-react";
import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { STATUS_FILTERS, type OrdersQuery } from "@/lib/admin-orders";

const field =
  "bg-surface text-ink border-field-line aria-invalid:border-danger h-11 w-full rounded-md border px-3 text-base";

/**
 * O filtro da lista: um formulário GET (funciona sem JavaScript). Com espaço (a largura do próprio formulário,
 * não da tela: ao lado da coluna do menu, 1024 px não bastam), uma linha só: a busca cresce, a situação e as
 * datas têm a largura delas.
 */
export function OrderFilters({ query }: { query: OrdersQuery }) {
  const active = !!(query.q || query.status || query.period.de || query.period.ate);
  const invalid = query.period.error ? true : undefined;
  return (
    <form
      method="get"
      action="/admin/pedidos"
      role="search"
      aria-label="Filtrar pedidos"
      className="@container space-y-2"
    >
      <div className="grid gap-3 @md:grid-cols-2 @4xl:grid-cols-[minmax(14rem,1fr)_13rem_10.5rem_10.5rem_auto] @4xl:items-end">
        <label className="space-y-1.5 @md:col-span-2 @2xl:col-span-1">
          <span className="block text-sm font-medium">Buscar</span>
          <input
            type="search"
            name="q"
            defaultValue={query.q}
            placeholder="Número, nome ou e-mail"
            maxLength={100}
            className={field}
          />
        </label>
        <label className="space-y-1.5 @md:col-span-2 @2xl:col-span-1">
          <span className="block text-sm font-medium">Situação</span>
          <select name="situacao" defaultValue={query.status ?? ""} className={field}>
            <option value="">Todas</option>
            {STATUS_FILTERS.map((s) => (
              <option key={s.value} value={s.value}>
                {s.label}
              </option>
            ))}
          </select>
        </label>
        <label className="space-y-1.5">
          <span className="block text-sm font-medium">De</span>
          <input
            type="date"
            name="de"
            defaultValue={query.period.de}
            aria-invalid={invalid}
            aria-describedby={invalid && "filtro-erro"}
            className={field}
          />
        </label>
        <label className="space-y-1.5">
          <span className="block text-sm font-medium">Até</span>
          <input
            type="date"
            name="ate"
            defaultValue={query.period.ate}
            aria-invalid={invalid}
            aria-describedby={invalid && "filtro-erro"}
            className={field}
          />
        </label>
        <div className="flex items-center gap-2 @md:col-span-2 @4xl:col-span-1">
          <button type="submit" className={buttonVariants({ variant: "primary" })}>
            <Search aria-hidden="true" />
            Filtrar
          </button>
          {active && (
            <Link href="/admin/pedidos" className={buttonVariants({ variant: "ghost" })}>
              Limpar
            </Link>
          )}
        </div>
      </div>
      {query.period.error && (
        <p id="filtro-erro" role="alert" className="text-danger text-sm font-medium">
          {query.period.error} A lista mostra todos os períodos.
        </p>
      )}
    </form>
  );
}
