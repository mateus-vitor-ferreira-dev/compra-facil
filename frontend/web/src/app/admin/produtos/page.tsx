import { Plus, Search } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { ProductStatus, STATUS_LABEL } from "@/components/admin/product-status";
import { FilterChip } from "@/components/store/filter-chip";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { getStock, listProducts, type AdminProduct } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { withFilter } from "@/lib/storefront";
import { cn } from "@/lib/utils";

export const metadata: Metadata = { title: "Produtos" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

const STATUSES = Object.keys(STATUS_LABEL) as AdminProduct["status"][];
// abaixo disto o disponível aparece em vermelho: hora de repor
const LOW_STOCK = 5;

/** Todos os produtos, em qualquer situação: busca, filtro por situação, preço e estoque disponível. */
export default async function AdminProducts({ searchParams }: Props) {
  const params = await searchParams;
  const { token } = await requireAdmin("/admin/produtos");
  const one = (k: string) => {
    const v = params[k];
    return (Array.isArray(v) ? v[0] : v)?.trim() || undefined;
  };
  const q = one("q");
  const status = STATUSES.find((s) => s === one("situacao"));
  const result = await listProducts(token, {
    q: q && q.length >= 2 ? q : undefined,
    status,
    cursor: one("cursor"),
  });
  const products = result.ok ? result.data.data : [];
  const stock = await getStock(products.map((p) => p.id));
  const path = "/admin/produtos";

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">Produtos</h1>
        <Link href="/admin/produtos/novo" className={buttonVariants()}>
          <Plus aria-hidden="true" />
          Novo produto
        </Link>
      </div>

      {one("removido") === "1" && (
        <p role="status" className="text-success font-semibold">
          Produto removido. Ele saiu da vitrine e o SKU ficou livre.
        </p>
      )}

      <div className="flex flex-wrap items-center gap-3">
        <form action={path} role="search" className="flex w-full max-w-md items-center gap-2">
          {status && <input type="hidden" name="situacao" value={status} />}
          <label htmlFor="busca-admin" className="sr-only">
            Buscar por nome, marca ou SKU
          </label>
          <input
            id="busca-admin"
            name="q"
            type="search"
            defaultValue={q}
            minLength={2}
            placeholder="Nome, marca ou SKU"
            className="bg-surface border-field-line h-11 min-w-0 flex-1 rounded-md border px-3"
          />
          <button type="submit" className={buttonVariants({ variant: "secondary", size: "icon" })}>
            <Search aria-hidden="true" />
            <span className="sr-only">Buscar</span>
          </button>
        </form>
        <ul aria-label="Situação" className="flex flex-wrap gap-2">
          <li>
            <FilterChip href={withFilter(path, params, "situacao", null)} active={!status}>
              Todos
            </FilterChip>
          </li>
          {STATUSES.map((s) => (
            <li key={s}>
              <FilterChip href={withFilter(path, params, "situacao", s)} active={status === s}>
                {STATUS_LABEL[s]}
              </FilterChip>
            </li>
          ))}
        </ul>
      </div>

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="A lista não carregou"
          description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
        />
      ) : products.length === 0 ? (
        <StateMessage
          title={q ? `Nenhum produto com “${q}”` : "Nenhum produto nesta situação"}
          description="Confira a busca ou cadastre um produto novo."
          action={
            <Link href="/admin/produtos/novo" className={buttonVariants({ variant: "secondary" })}>
              Novo produto
            </Link>
          }
        />
      ) : (
        <div
          role="region"
          aria-label="Tabela de produtos (role para os lados no celular)"
          tabIndex={0}
          className="border-line overflow-x-auto rounded-md border"
        >
          <table className="w-full min-w-[48rem] text-left text-sm">
            <caption className="sr-only">Produtos{q ? ` com “${q}”` : ""}</caption>
            <thead className="bg-surface border-line text-ink-soft border-b">
              <tr>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Produto
                </th>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Categoria
                </th>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Marca
                </th>
                <th scope="col" className="px-3 py-2.5 text-right font-semibold">
                  Preço
                </th>
                <th scope="col" className="px-3 py-2.5 text-right font-semibold">
                  Disponível
                </th>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Situação
                </th>
              </tr>
            </thead>
            <tbody className="divide-line divide-y">
              {products.map((p) => {
                const level = stock[p.id];
                const low = level !== undefined && level.available <= LOW_STOCK;
                const onSale = p.effective_price.amount_cents < p.price.amount_cents;
                return (
                  <tr key={p.id} className="hover:bg-ink/4">
                    <td className="px-3 py-2">
                      <Link href={`/admin/produtos/${p.id}`} className="font-medium hover:underline">
                        {p.name}
                      </Link>
                      <div className="text-ink-soft text-xs">{p.sku}</div>
                    </td>
                    <td className="text-ink-soft px-3 py-2">{p.category.name}</td>
                    <td className="text-ink-soft px-3 py-2">{p.brand ?? "–"}</td>
                    <td className="font-condensed px-3 py-2 text-right text-base font-bold whitespace-nowrap">
                      {formatMoney(p.effective_price)}
                      {onSale && (
                        <div className="text-ink-soft text-xs font-normal">
                          <s>{formatMoney(p.price)}</s> em campanha
                        </div>
                      )}
                    </td>
                    <td
                      className={cn(
                        "font-condensed px-3 py-2 text-right text-base font-bold",
                        low && "text-danger",
                      )}
                    >
                      {level === undefined ? "–" : level.available}
                      {low && <div className="text-xs font-normal">repor</div>}
                    </td>
                    <td className="px-3 py-2">
                      <ProductStatus status={p.status} />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      )}

      {result.ok && result.data.page.has_more && result.data.page.next_cursor && (
        <Link
          href={withFilter(path, params, "cursor", result.data.page.next_cursor)}
          className={buttonVariants({ variant: "secondary" })}
        >
          Próximos produtos
        </Link>
      )}
    </div>
  );
}
