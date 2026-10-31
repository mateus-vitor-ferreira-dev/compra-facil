import { CircleCheck, Search } from "lucide-react";
import type { Metadata } from "next";
import { notFound } from "next/navigation";
import { z } from "zod";
import { AddProductsForm } from "@/components/admin/add-products-form";
import { CampaignForm } from "@/components/admin/campaign-form";
import { CampaignPhase } from "@/components/admin/campaign-phase";
import { STATUS_LABEL } from "@/components/admin/product-status";
import { Breadcrumbs } from "@/components/store/breadcrumbs";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { campaignProducts, getCampaign, listProducts } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatPeriod, toLocalInput } from "@/lib/campaign-time";
import { formatMoney } from "@/lib/format";
import { removeCampaign, removeCampaignProduct } from "../actions";

export const metadata: Metadata = { title: "Campanha" };

type Props = {
  params: Promise<{ id: string }>;
  searchParams: Promise<Record<string, string | string[] | undefined>>;
};

/** Uma campanha: os produtos dela (incluir e retirar), o período e o desconto, e a remoção. */
export default async function AdminCampaign({ params, searchParams }: Props) {
  const [{ id }, query] = await Promise.all([params, searchParams]);
  if (!z.uuid().safeParse(id).success) notFound();
  const { token } = await requireAdmin(`/admin/campanhas/${id}`);
  const search = typeof query.busca === "string" ? query.busca.trim() : "";
  const [campaign, products, found] = await Promise.all([
    getCampaign(token, id),
    campaignProducts(token, id),
    search.length >= 2 ? listProducts(token, { q: search }) : Promise.resolve(undefined),
  ]);
  if (!campaign.ok && campaign.error.status === 404) notFound();
  if (!campaign.ok || !products.ok) {
    return (
      <StateMessage
        tone="error"
        title="A campanha não carregou"
        description="O catálogo não respondeu agora. Tente de novo em instantes."
      />
    );
  }
  const c = campaign.data;
  const inCampaign = new Set(products.data.data.map((p) => p.id));
  const candidates = found?.ok ? found.data.data.filter((p) => !inCampaign.has(p.id)) : [];
  const notice =
    query.salva === "1"
      ? "Campanha salva. A vitrine já mostra os preços novos."
      : typeof query.incluidos === "string"
        ? `${query.incluidos === "1" ? "1 produto incluído" : `${query.incluidos} produtos incluídos`} na campanha.`
        : query.retirado === "1"
          ? "Produto retirado da campanha: volta ao preço de tabela."
          : undefined;
  const failure =
    query.erro === "retirar"
      ? "O produto não foi retirado. Tente de novo."
      : query.erro === "remover"
        ? "A campanha não foi removida. Tente de novo."
        : undefined;

  return (
    <div className="space-y-5">
      <Breadcrumbs items={[{ href: "/admin/campanhas", label: "Campanhas" }, { label: c.name }]} />
      <div className="flex flex-wrap items-center gap-3">
        <h1 className="text-2xl font-semibold tracking-tight">{c.name}</h1>
        <CampaignPhase campaign={c} />
      </div>
      <p className="text-ink-soft">
        <span className="font-condensed text-ink text-xl font-extrabold">{c.discount_percent}%</span> de
        desconto, {formatPeriod(c)}
      </p>
      {notice && (
        <p role="status" className="text-success flex items-center gap-2 font-semibold">
          <CircleCheck aria-hidden="true" className="size-5" />
          {notice}
        </p>
      )}
      {failure && (
        <p role="alert" className="text-danger font-semibold">
          {failure}
        </p>
      )}

      <div className="grid gap-10 lg:grid-cols-[minmax(0,1fr)_22rem]">
        <div className="space-y-8">
          <section aria-labelledby="produtos" className="space-y-3">
            <h2 id="produtos" className="text-xl font-semibold tracking-tight">
              Produtos na campanha ({products.data.data.length})
            </h2>
            {products.data.data.length === 0 ? (
              <p className="text-ink-soft">Nenhum produto ainda. Busque abaixo e inclua os que entram.</p>
            ) : (
              <ul className="divide-line border-line divide-y rounded-md border">
                {products.data.data.map((p) => (
                  <li key={p.id} className="flex min-h-11 items-center gap-3 px-3 py-2">
                    <span className="min-w-0 flex-1">
                      <span className="block truncate font-medium">{p.name}</span>
                      <span className="text-ink-soft block text-xs">
                        {p.sku}, {STATUS_LABEL[p.status]}
                      </span>
                    </span>
                    <span className="text-right whitespace-nowrap">
                      <span className="font-condensed block font-bold">{formatMoney(p.effective_price)}</span>
                      <s className="text-ink-soft block text-xs">{formatMoney(p.price)}</s>
                    </span>
                    <form action={removeCampaignProduct}>
                      <input type="hidden" name="id" value={c.id} />
                      <input type="hidden" name="product_id" value={p.id} />
                      <SubmitButton
                        variant="ghost"
                        size="sm"
                        pendingLabel="Retirando…"
                        aria-label={`Retirar ${p.name} da campanha`}
                      >
                        Retirar
                      </SubmitButton>
                    </form>
                  </li>
                ))}
              </ul>
            )}
          </section>

          <section aria-labelledby="incluir" className="space-y-3">
            <h2 id="incluir" className="text-xl font-semibold tracking-tight">
              Incluir produtos
            </h2>
            <form
              action={`/admin/campanhas/${c.id}`}
              role="search"
              className="flex max-w-md items-center gap-2"
            >
              <label htmlFor="busca-campanha" className="sr-only">
                Buscar produtos por nome, marca ou SKU
              </label>
              <input
                id="busca-campanha"
                name="busca"
                type="search"
                defaultValue={search}
                minLength={2}
                placeholder="Nome, marca ou SKU"
                className="bg-surface border-field-line h-11 min-w-0 flex-1 rounded-md border px-3"
              />
              <button type="submit" className={buttonVariants({ variant: "secondary", size: "icon" })}>
                <Search aria-hidden="true" />
                <span className="sr-only">Buscar</span>
              </button>
            </form>
            {search.length >= 2 &&
              (candidates.length === 0 ? (
                <p className="text-ink-soft">
                  Nenhum produto fora da campanha com “{search}”. Tente o SKU ou outra palavra.
                </p>
              ) : (
                <AddProductsForm
                  campaignId={c.id}
                  candidates={candidates.map((p) => ({
                    id: p.id,
                    name: p.name,
                    sku: p.sku,
                    status: STATUS_LABEL[p.status],
                    price: formatMoney(p.price),
                  }))}
                />
              ))}
          </section>
        </div>

        <aside className="space-y-8 lg:sticky lg:top-6 lg:self-start">
          <section aria-labelledby="dados" className="space-y-4">
            <h2 id="dados" className="text-xl font-semibold tracking-tight">
              Período e desconto
            </h2>
            <CampaignForm
              initial={{
                id: c.id,
                name: c.name,
                slug: c.slug,
                discount: String(c.discount_percent),
                starts: toLocalInput(c.starts_at),
                ends: toLocalInput(c.ends_at),
              }}
            />
          </section>
          <details className="border-line rounded-md border p-4">
            <summary className="text-danger cursor-pointer font-semibold">Remover campanha</summary>
            <form action={removeCampaign} className="mt-3 space-y-3">
              <input type="hidden" name="id" value={c.id} />
              <p className="text-ink-soft text-sm">
                Os produtos voltam ao preço de tabela na hora. Pedidos já feitos mantêm o desconto. Para
                encerrar antes, mude o fim para agora.
              </p>
              <SubmitButton variant="danger" size="sm" pendingLabel="Removendo…">
                Remover {c.name}
              </SubmitButton>
            </form>
          </details>
        </aside>
      </div>
    </div>
  );
}
