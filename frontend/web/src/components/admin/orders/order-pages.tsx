import Link from "next/link";
import { buttonVariants } from "@/components/ui/button";
import { filterSearch, type OrdersQuery } from "@/lib/admin-orders";

/** A lista vem de 50 em 50: o próximo bloco e a volta ao começo, pelo cursor da API. */
export function OrderPages({ query, next }: { query: OrdersQuery; next?: string | null }) {
  if (!next && !query.cursor) return null;
  return (
    <nav aria-label="Páginas da lista" className="flex flex-wrap gap-3">
      {query.cursor && (
        <Link href={`/admin/pedidos${filterSearch(query)}`} className={buttonVariants({ variant: "ghost" })}>
          Voltar aos mais recentes
        </Link>
      )}
      {next && (
        <Link
          href={`/admin/pedidos${filterSearch(query, { depois: next })}`}
          className={buttonVariants({ variant: "secondary" })}
        >
          Ver os próximos 50
        </Link>
      )}
    </nav>
  );
}
