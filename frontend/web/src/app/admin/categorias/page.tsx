import { CircleCheck } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { CategoryForm } from "@/components/admin/category-form";
import { StateMessage } from "@/components/ui/state";
import { listCategories } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";

export const metadata: Metadata = { title: "Categorias" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** Os departamentos e as categorias de cada um, e a criação de uma nova. */
export default async function AdminCategories({ searchParams }: Props) {
  await requireAdmin("/admin/categorias");
  const [result, query] = await Promise.all([listCategories(), searchParams]);
  const all = result.ok ? result.data.data : [];
  const roots = all.filter((c) => !c.parent_id);
  const created = typeof query.criada === "string" ? query.criada : undefined;

  return (
    <div className="space-y-5">
      <h1 className="text-2xl font-semibold tracking-tight">Categorias</h1>
      {created && (
        <p role="status" className="text-success flex items-center gap-2 font-semibold">
          <CircleCheck aria-hidden="true" className="size-5" />
          Categoria “{created}” criada.
        </p>
      )}
      <div className="grid gap-10 lg:grid-cols-[minmax(0,1fr)_22rem]">
        {!result.ok ? (
          <StateMessage
            tone="error"
            title="As categorias não carregaram"
            description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
          />
        ) : (
          <ul className="divide-line border-line divide-y rounded-md border">
            {roots.map((root) => {
              const children = all.filter((c) => c.parent_id === root.id);
              return (
                <li key={root.id} className="space-y-2 p-4">
                  <div className="flex flex-wrap items-baseline justify-between gap-2">
                    <h2 className="text-lg font-semibold">{root.name}</h2>
                    <Link
                      href={`/categoria/${root.slug}`}
                      className="text-ink-soft text-sm underline-offset-4 hover:underline"
                    >
                      /categoria/{root.slug}
                    </Link>
                  </div>
                  {children.length > 0 ? (
                    <ul className="flex flex-wrap gap-2">
                      {children.map((c) => (
                        <li key={c.id}>
                          <Link
                            href={`/categoria/${c.slug}`}
                            className="bg-ink/6 hover:bg-ink/12 inline-flex min-h-9 items-center rounded-full px-3 text-sm"
                          >
                            {c.name}
                          </Link>
                        </li>
                      ))}
                    </ul>
                  ) : (
                    <p className="text-ink-soft text-sm">Sem categorias dentro.</p>
                  )}
                </li>
              );
            })}
          </ul>
        )}
        <section aria-labelledby="nova" className="space-y-4 lg:sticky lg:top-6 lg:self-start">
          <h2 id="nova" className="text-xl font-semibold tracking-tight">
            Nova categoria
          </h2>
          <CategoryForm roots={roots.map(({ id, name }) => ({ id, name }))} />
        </section>
      </div>
    </div>
  );
}
