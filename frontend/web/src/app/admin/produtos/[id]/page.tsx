import { CircleCheck } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { notFound } from "next/navigation";
import { z } from "zod";
import { ProductForm } from "@/components/admin/product-form";
import { ProductStatus } from "@/components/admin/product-status";
import { StockForm } from "@/components/admin/stock-form";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { getProduct, getStock } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { reaisInput } from "@/lib/money-input";
import { removeProduct } from "../actions";
import { categoryOptions } from "../category-options";

export const metadata: Metadata = { title: "Produto" };

type Props = {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/** Um produto: os dados (com If-Match), o estoque com o ajuste por motivo e a remoção. */
export default async function AdminProduct({ params, searchParams }: Props) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  if (!z.uuid().safeParse(id).success) notFound();
  const { token } = await requireAdmin(`/admin/produtos/${id}`);
  const [result, categories, stock] = await Promise.all([
    getProduct(token, id),
    categoryOptions(),
    getStock([id]),
  ]);
  if (!result.ok && result.error.status === 404) notFound();
  if (!result.ok) {
    return (
      <StateMessage
        tone="error"
        title="O produto não carregou"
        description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
      />
    );
  }
  const p = result.data;
  const level = stock[id];
  const notice =
    query.salvo === "1"
      ? "Produto salvo. A vitrine já mostra a versão nova."
      : typeof query.estoque === "string"
        ? `Estoque ajustado: ${query.estoque} unidades em estoque.`
        : undefined;

  return (
    <div className="space-y-5">
      <Breadcrumbs items={[{ href: "/admin/produtos", label: "Produtos" }, { label: p.name }]} />
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{p.name}</h1>
        <ProductStatus status={p.status} />
        {p.status === "active" && (
          <Link href={`/produto/${p.id}`} className="text-sm font-semibold underline underline-offset-4">
            Ver na loja
          </Link>
        )}
      </div>
      {notice && (
        <p role="status" className="text-success flex items-center gap-2 font-semibold">
          <CircleCheck aria-hidden="true" className="size-5" />
          {notice}
        </p>
      )}
      {query.erro === "remover" && (
        <p role="alert" className="text-danger font-semibold">
          O produto não foi removido. Tente de novo.
        </p>
      )}

      <div className="grid gap-10 lg:grid-cols-[minmax(0,1fr)_20rem]">
        <ProductForm
          categories={categories}
          initial={{
            id: p.id,
            etag: result.etag ?? `"${p.version}"`,
            name: p.name,
            sku: p.sku,
            brand: p.brand ?? "",
            description: p.description ?? "",
            category_id: p.category.id,
            image_url: p.image_url ?? "",
            price: reaisInput(p.price.amount_cents),
            status: p.status,
          }}
        />

        <aside className="space-y-8 lg:sticky lg:top-6 lg:self-start">
          <section aria-labelledby="estoque" className="space-y-4">
            <h2 id="estoque" className="text-xl font-semibold tracking-tight">
              Estoque
            </h2>
            {level ? (
              <dl className="border-line divide-line grid grid-cols-3 divide-x rounded-md border text-center">
                {(
                  [
                    ["Em estoque", level.on_hand],
                    ["Reservado", level.reserved],
                    ["Disponível", level.available],
                  ] as const
                ).map(([label, n]) => (
                  <div key={label} className="px-2 py-3">
                    <dt className="text-ink-soft text-xs">{label}</dt>
                    <dd className="font-condensed text-2xl font-extrabold">{n}</dd>
                  </div>
                ))}
              </dl>
            ) : (
              <p className="text-ink-soft text-sm">Sem saldo registrado: o primeiro ajuste cria.</p>
            )}
            <p className="text-ink-soft text-sm">
              Reservado são unidades presas a pedidos em andamento; a venda usa o disponível.
            </p>
            <StockForm productId={p.id} idempotencyKey={crypto.randomUUID()} />
          </section>

          <section aria-labelledby="campanha" className="space-y-1">
            <h2 id="campanha" className="text-xl font-semibold tracking-tight">
              Preço na vitrine
            </h2>
            <p className="font-condensed text-2xl font-extrabold">{formatMoney(p.effective_price)}</p>
            <p className="text-ink-soft text-sm">
              {p.campaign
                ? `Com ${p.campaign.discount_percent}% da campanha ${p.campaign.name}.`
                : "Sem campanha agora: o preço de tabela."}
            </p>
          </section>

          <details className="border-line rounded-md border p-4">
            <summary className="text-danger cursor-pointer font-semibold">Remover produto</summary>
            <form action={removeProduct} className="mt-3 space-y-3">
              <input type="hidden" name="id" value={p.id} />
              <p className="text-ink-soft text-sm">
                O produto sai da vitrine e das listas do painel, e o SKU fica livre. Pedidos já feitos
                continuam com o nome e o preço deles. Para só tirar de venda, mude a situação para &quot;Fora
                de venda&quot;.
              </p>
              <SubmitButton variant="danger" size="sm" pendingLabel="Removendo…">
                Remover {p.sku}
              </SubmitButton>
            </form>
          </details>
        </aside>
      </div>
    </div>
  );
}
