import { ChevronLeft, ChevronRight } from "lucide-react";
import Link from "next/link";
import { pageItems } from "@/lib/pagination";
import { withFilter } from "@/lib/storefront";
import { cn } from "@/lib/utils";

type Params = Record<string, string | string[] | undefined>;

/**
 * As páginas de uma listagem: Anterior, os números (a primeira, a última, a atual e as vizinhas, com
 * "…" nos saltos) e Próxima. Links comuns com ?pagina=: funcionam sem JavaScript e voltam pelo botão do
 * navegador. No celular, só Anterior, "Página 3 de 42" e Próxima.
 */
export function Pagination({
  path,
  params,
  current,
  total,
}: {
  path: string;
  params: Params;
  current: number;
  total: number;
}) {
  if (total <= 1) return null;
  const href = (n: number) => withFilter(path, params, "pagina", n === 1 ? null : String(n));
  const step =
    "inline-flex min-h-11 items-center gap-1 rounded-md px-3 font-semibold hover:bg-ink/8 sm:min-h-10 [&_svg]:size-4";
  // na primeira página não há "Anterior" (nem "Próxima" na última): o espaço fica, invisível, para o
  // resto não pular de lugar
  const off = "invisible";

  return (
    <nav
      aria-label="Páginas de produtos"
      className="flex items-center justify-between gap-2 pt-4 sm:justify-center"
    >
      {current > 1 ? (
        <Link href={href(current - 1)} rel="prev" className={step}>
          <ChevronLeft aria-hidden="true" />
          Anterior
        </Link>
      ) : (
        <span aria-hidden="true" className={cn(step, off)}>
          <ChevronLeft />
          Anterior
        </span>
      )}

      <p className="text-ink-soft text-sm sm:hidden">
        Página <span className="font-condensed text-ink font-bold tabular-nums">{current}</span> de{" "}
        <span className="font-condensed text-ink font-bold tabular-nums">{total}</span>
      </p>
      <ol className="hidden items-center gap-1 sm:flex">
        {pageItems(current, total).map((item, i) =>
          item === "gap" ? (
            <li key={`gap-${i}`} aria-hidden="true" className="text-ink-soft w-8 text-center">
              …
            </li>
          ) : (
            <li key={item}>
              <Link
                href={href(item)}
                aria-current={item === current ? "page" : undefined}
                aria-label={`Página ${item}`}
                className={cn(
                  "font-condensed grid size-10 place-items-center rounded-md text-lg font-bold tabular-nums",
                  item === current ? "bg-ink text-paper" : "hover:bg-ink/8",
                )}
              >
                {item}
              </Link>
            </li>
          ),
        )}
      </ol>

      {current < total ? (
        <Link href={href(current + 1)} rel="next" className={step}>
          Próxima
          <ChevronRight aria-hidden="true" />
        </Link>
      ) : (
        <span aria-hidden="true" className={cn(step, off)}>
          Próxima
          <ChevronRight />
        </span>
      )}
    </nav>
  );
}
