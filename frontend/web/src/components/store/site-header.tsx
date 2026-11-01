import { ChevronDown, ShoppingCart, User } from "lucide-react";
import Link from "next/link";
import { Logo } from "@/components/brand/logo";
import { CategoryIcon } from "@/components/commerce/category-icon";
import type { categoryTree } from "@/lib/catalog-api";
import { HeaderSearch } from "./search-box";

type Tree = ReturnType<typeof categoryTree>;

const iconLink =
  "text-ink hover:bg-ink/8 inline-flex h-11 min-w-11 items-center justify-center gap-2 rounded-md px-2 font-medium sm:px-3";

/**
 * O cabeçalho da loja. No celular: logo e atalhos em cima, a busca inteira embaixo e as categorias num
 * menu que abre sem JavaScript (details). No desktop: tudo numa linha e as categorias à mostra.
 */
export function SiteHeader({
  categories,
  cartCount,
  signedIn,
}: {
  categories: Tree;
  cartCount: number;
  signedIn: boolean;
}) {
  return (
    <header className="bg-paper border-line border-b">
      <div className="mx-auto grid max-w-6xl grid-cols-[auto_1fr] items-center gap-x-4 gap-y-2 px-4 py-2 sm:px-6 md:grid-cols-[auto_1fr_auto] md:py-3">
        <Link href="/" className="rounded-md">
          <Logo />
          <span className="sr-only">: página inicial</span>
        </Link>

        {/* no desktop a ordem do teclado é a visual (logo, busca, atalhos); no celular a busca desce */}
        <HeaderSearch className="order-last col-span-2 md:order-none md:col-span-1" />

        <nav aria-label="Atalhos" className="flex items-center justify-end gap-1">
          <Link href="/ofertas" className={`${iconLink} hidden sm:inline-flex`}>
            Ofertas
          </Link>
          <Link href={signedIn ? "/conta" : "/entrar"} className={iconLink}>
            <User aria-hidden="true" className="size-5" />
            <span className="sr-only sm:not-sr-only">{signedIn ? "Minha conta" : "Entrar"}</span>
          </Link>
          <Link href="/carrinho" className={`${iconLink} relative`}>
            <ShoppingCart aria-hidden="true" className="size-5" />
            <span className="sr-only sm:not-sr-only">Carrinho</span>
            {cartCount > 0 && (
              <span
                aria-hidden="true"
                className="bg-ink text-paper font-condensed absolute top-0.5 left-6 min-w-5 rounded-full px-1 text-center text-xs leading-5 font-bold sm:static sm:-ml-1"
              >
                {cartCount > 99 ? "99+" : cartCount}
              </span>
            )}
            <span className="sr-only">
              {cartCount === 0 ? "vazio" : `com ${cartCount} ${cartCount === 1 ? "item" : "itens"}`}
            </span>
          </Link>
        </nav>
      </div>

      {categories.length > 0 && (
        <nav aria-label="Categorias" className="mx-auto max-w-6xl px-4 sm:px-6 md:pb-2">
          {/* celular: um menu; desktop: a lista à mostra */}
          <details className="group md:hidden">
            <summary className="text-ink flex h-11 cursor-pointer list-none items-center gap-2 font-medium [&::-webkit-details-marker]:hidden">
              Categorias
              <ChevronDown aria-hidden="true" className="size-4 transition-transform group-open:rotate-180" />
            </summary>
            <ul className="grid grid-cols-1 gap-x-4 pb-3 min-[420px]:grid-cols-2">
              <li>
                <Link href="/ofertas" className="flex min-h-11 items-center font-semibold">
                  Ofertas
                </Link>
              </li>
              {categories.map((c) => (
                <li key={c.id}>
                  <Link href={`/categoria/${c.slug}`} className="flex min-h-11 items-center gap-2">
                    <CategoryIcon slug={c.slug} className="text-ink-soft size-5" />
                    {c.name}
                  </Link>
                </li>
              ))}
            </ul>
          </details>
          {/* sempre uma linha: em telas maiores tudo cabe, e nas menores a lista rola para o lado */}
          <ul className="hidden [scrollbar-width:none] gap-x-3 overflow-x-auto md:flex xl:justify-between [&::-webkit-scrollbar]:hidden">
            {categories.map((c) => (
              <li key={c.id} className="shrink-0">
                <Link
                  href={`/categoria/${c.slug}`}
                  className="text-ink-soft hover:text-ink flex h-10 items-center text-sm font-medium whitespace-nowrap focus-visible:-outline-offset-2"
                >
                  {c.name}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      )}
    </header>
  );
}
