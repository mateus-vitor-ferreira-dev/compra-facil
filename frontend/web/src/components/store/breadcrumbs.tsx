import { ChevronLeft, ChevronRight } from "lucide-react";
import Link from "next/link";

/**
 * A trilha até a página atual; o último item é a própria página (sem link). No celular, só o nível acima,
 * numa linha: a trilha inteira empurrava o conteúdo para fora da primeira tela.
 */
export function Breadcrumbs({ items }: { items: { href?: string; label: string }[] }) {
  const parent = items.findLastIndex((item) => item.href);
  return (
    <nav aria-label="Você está em">
      <ol className="text-ink-soft flex flex-wrap items-center gap-1 text-sm">
        {items.map((item, i) => (
          // a posição na trilha é estável, e o nome sozinho repete (Monitores › Monitores: a subcategoria e o tipo)
          <li
            key={`${i}-${item.label}`}
            className={i === parent ? "flex items-center gap-1" : "hidden items-center gap-1 sm:flex"}
          >
            {i > 0 && <ChevronRight aria-hidden="true" className="hidden size-4 sm:block" />}
            {i === parent && <ChevronLeft aria-hidden="true" className="size-4 sm:hidden" />}
            {item.href ? (
              <Link
                href={item.href}
                className="hover:text-ink inline-flex min-h-11 items-center underline-offset-4 hover:underline sm:min-h-8"
              >
                {item.label}
              </Link>
            ) : (
              <span aria-current="page" className="text-ink">
                {item.label}
              </span>
            )}
          </li>
        ))}
      </ol>
    </nav>
  );
}
