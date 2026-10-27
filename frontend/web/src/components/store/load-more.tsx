"use client";

import { useState } from "react";
import { ProductCard } from "@/components/commerce/product-card";
import { buttonVariants } from "@/components/ui/button";
import type { Product } from "@/lib/catalog";
import { cn } from "@/lib/utils";

type Page = { data: Product[]; page: { next_cursor?: string | null; has_more: boolean } };

/**
 * "Ver mais produtos": busca a próxima página pelo cursor (GET /api/produtos) e acrescenta à grade,
 * levando o foco ao primeiro produto novo. Sem JavaScript, é um link para a próxima página.
 */
export function LoadMore({
  query,
  cursor,
  pageHref,
}: {
  /** os filtros da listagem, como a API recebe (q, category, on_sale, brand, preço, sort) */
  query: Record<string, string>;
  cursor: string;
  /** a próxima página por link, para quem está sem JavaScript */
  pageHref: string;
}) {
  const [items, setItems] = useState<Product[]>([]);
  const [next, setNext] = useState<string | null>(cursor);
  const [state, setState] = useState<"idle" | "loading" | "error">("idle");

  async function load(event: React.MouseEvent) {
    if (!next || state === "loading") return;
    event.preventDefault();
    setState("loading");
    try {
      const res = await fetch(`/api/produtos?${new URLSearchParams({ ...query, cursor: next })}`);
      if (!res.ok) throw new Error(String(res.status));
      const page = (await res.json()) as Page;
      const firstNew = page.data[0]?.id;
      setItems((current) => [...current, ...page.data]);
      setNext(page.page.has_more ? (page.page.next_cursor ?? null) : null);
      setState("idle");
      // o foco vai para o primeiro produto novo, para o teclado continuar de onde parou
      requestAnimationFrame(() =>
        document.querySelector<HTMLAnchorElement>(`[data-product="${firstNew}"] a`)?.focus(),
      );
    } catch {
      setState("error");
    }
  }

  return (
    <>
      {items.map((p) => (
        <li key={p.id} data-product={p.id}>
          <ProductCard product={p} />
        </li>
      ))}
      <li className="col-span-full flex flex-col items-center gap-3 pt-4">
        {state === "error" && (
          <p role="alert" className="text-danger text-sm">
            Os próximos produtos não carregaram. Tente de novo.
          </p>
        )}
        {next && (
          <a
            href={pageHref}
            onClick={load}
            aria-disabled={state === "loading" || undefined}
            className={cn(buttonVariants({ variant: "secondary" }), "min-w-48")}
          >
            {state === "loading" ? "Carregando…" : "Ver mais produtos"}
          </a>
        )}
        <p aria-live="polite" className="sr-only">
          {state === "idle" && items.length > 0 ? `${items.length} produtos a mais na lista` : ""}
        </p>
      </li>
    </>
  );
}
