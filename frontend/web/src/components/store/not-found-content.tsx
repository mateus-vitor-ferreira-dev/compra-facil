import Link from "next/link";
import { MissingTag } from "@/components/brand/missing-tag";
import { CategoryIcon } from "@/components/commerce/category-icon";
import { buttonVariants } from "@/components/ui/button";
import { categoryTree, getCategories } from "@/lib/catalog-api";

/**
 * A página que não existe, na loja: a etiqueta 404 pendurada, o que provavelmente aconteceu e por onde voltar
 * à compra. A busca não se repete aqui: está no cabeçalho, logo acima.
 */
export async function NotFoundContent() {
  const categories = await getCategories();
  const tree = categories.ok ? categoryTree(categories.data).slice(0, 8) : [];

  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 py-10 sm:px-6 lg:py-16">
      <div className="grid items-center gap-10 md:grid-cols-[minmax(0,1fr)_auto] md:gap-16">
        <MissingTag className="justify-self-center md:order-last" />
        <div className="max-w-xl space-y-6">
          <div className="space-y-3">
            <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">
              Esta página não está na prateleira
            </h1>
            <p className="text-ink-soft text-lg">
              O endereço pode ter mudado, ou o produto saiu do catálogo. Busque pelo nome no alto da página,
              ou comece pelas ofertas.
            </p>
          </div>
          <div className="flex flex-wrap gap-3">
            <Link href="/ofertas" className={buttonVariants()}>
              Ver as ofertas
            </Link>
            <Link href="/" className={buttonVariants({ variant: "secondary" })}>
              Ir para o início
            </Link>
          </div>
          {tree.length > 0 && (
            <nav aria-labelledby="nao-encontrada-categorias" className="space-y-3 pt-2">
              <h2 id="nao-encontrada-categorias" className="font-semibold">
                Ou comece por uma categoria
              </h2>
              <ul className="flex flex-wrap gap-2">
                {tree.map((c) => (
                  <li key={c.id}>
                    <Link
                      href={`/categoria/${c.slug}`}
                      className="border-line hover:border-ink/60 bg-surface inline-flex min-h-11 items-center gap-2 rounded-md border px-3 text-sm font-medium pointer-fine:min-h-9"
                    >
                      <CategoryIcon slug={c.slug} className="text-ink-soft size-4" />
                      {c.name}
                    </Link>
                  </li>
                ))}
              </ul>
            </nav>
          )}
        </div>
      </div>
    </main>
  );
}
