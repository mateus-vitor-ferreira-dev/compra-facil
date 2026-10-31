import Link from "next/link";
import { Logo } from "@/components/brand/logo";
import { brand } from "@/lib/brand";
import type { categoryTree } from "@/lib/catalog-api";
import { company } from "@/lib/legal";
import { CompanyData } from "./company-data";
import { POLICIES } from "./policy-page";

type Tree = ReturnType<typeof categoryTree>;

/** O rodapé: as categorias, a conta e o aviso honesto de que esta é uma loja de demonstração. */
export function SiteFooter({ categories }: { categories: Tree }) {
  return (
    <footer className="border-line mt-16 border-t">
      <div className="mx-auto grid max-w-6xl gap-10 px-4 py-10 sm:px-6 md:grid-cols-[2fr_1fr_1fr_1fr]">
        <div className="max-w-sm space-y-3">
          <Logo />
          <p className="text-ink-soft">
            Loja de demonstração: os produtos, os pedidos e os pagamentos são simulados. Nada é cobrado nem
            entregue.
          </p>
          <CompanyData {...company} />
        </div>
        {categories.length > 0 && (
          <nav aria-labelledby="rodape-categorias">
            <h2 id="rodape-categorias" className="font-semibold">
              Categorias
            </h2>
            <ul className="mt-2">
              {categories.map((c) => (
                <li key={c.id}>
                  <Link
                    href={`/categoria/${c.slug}`}
                    className="text-ink-soft hover:text-ink flex min-h-11 items-center sm:min-h-9"
                  >
                    {c.name}
                  </Link>
                </li>
              ))}
            </ul>
          </nav>
        )}
        <nav aria-labelledby="rodape-loja">
          <h2 id="rodape-loja" className="font-semibold">
            Loja
          </h2>
          <ul className="mt-2">
            {(
              [
                ["/ofertas", "Ofertas"],
                ["/conta", "Minha conta"],
                ["/carrinho", "Carrinho"],
              ] as const
            ).map(([href, label]) => (
              <li key={href}>
                <Link
                  href={href}
                  className="text-ink-soft hover:text-ink flex min-h-11 items-center sm:min-h-9"
                >
                  {label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
        <nav aria-labelledby="rodape-ajuda">
          <h2 id="rodape-ajuda" className="font-semibold">
            Ajuda
          </h2>
          <ul className="mt-2">
            {[{ href: "/acompanhar", label: "Acompanhar compra" }, ...POLICIES].map(({ href, label }) => (
              <li key={href}>
                <Link
                  href={href}
                  className="text-ink-soft hover:text-ink flex min-h-11 items-center sm:min-h-9"
                >
                  {label}
                </Link>
              </li>
            ))}
          </ul>
        </nav>
      </div>
      <p className="text-ink-soft mx-auto max-w-6xl px-4 pb-8 text-sm sm:px-6">
        © {new Date().getFullYear()} {brand.name}
      </p>
    </footer>
  );
}
