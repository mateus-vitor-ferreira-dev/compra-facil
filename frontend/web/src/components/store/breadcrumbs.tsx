import { ChevronRight } from "lucide-react";
import Link from "next/link";

/** A trilha até a página atual; o último item é a própria página (sem link). */
export function Breadcrumbs({ items }: { items: { href?: string; label: string }[] }) {
  return (
    <nav aria-label="Você está em">
      <ol className="text-ink-soft flex flex-wrap items-center gap-1 text-sm">
        {items.map((item, i) => (
          // a posição na trilha é estável, e o nome sozinho repete (Monitores › Monitores: a subcategoria e o tipo)
          <li key={`${i}-${item.label}`} className="flex items-center gap-1">
            {i > 0 && <ChevronRight aria-hidden="true" className="size-4" />}
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
