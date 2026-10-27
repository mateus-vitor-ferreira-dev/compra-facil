import { CircleCheck, Plus } from "lucide-react";
import type { Metadata } from "next";
import Link from "next/link";
import { CampaignPhase } from "@/components/admin/campaign-phase";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import { listCampaigns } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatPeriod } from "@/lib/campaign-time";

export const metadata: Metadata = { title: "Campanhas" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/** As campanhas passadas, no ar e agendadas, com o período, o desconto e quantos produtos têm. */
export default async function AdminCampaigns({ searchParams }: Props) {
  const query = await searchParams;
  const { token } = await requireAdmin("/admin/campanhas");
  const cursor = typeof query.cursor === "string" ? query.cursor : undefined;
  const result = await listCampaigns(token, cursor);

  return (
    <div className="space-y-5">
      <div className="flex flex-wrap items-end justify-between gap-4">
        <h1 className="text-2xl font-semibold tracking-tight">Campanhas</h1>
        <Link href="/admin/campanhas/nova" className={buttonVariants()}>
          <Plus aria-hidden="true" />
          Nova campanha
        </Link>
      </div>
      {query.removida === "1" && (
        <p role="status" className="text-success flex items-center gap-2 font-semibold">
          <CircleCheck aria-hidden="true" className="size-5" />
          Campanha removida. Os produtos dela voltaram ao preço de tabela.
        </p>
      )}

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="As campanhas não carregaram"
          description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
        />
      ) : result.data.data.length === 0 ? (
        <StateMessage
          title="Nenhuma campanha ainda"
          description="Crie a primeira: escolha o período e o desconto, depois os produtos."
          action={
            <Link href="/admin/campanhas/nova" className={buttonVariants({ variant: "secondary" })}>
              Nova campanha
            </Link>
          }
        />
      ) : (
        <div
          role="region"
          aria-label="Tabela de campanhas (role para os lados no celular)"
          tabIndex={0}
          className="border-line overflow-x-auto rounded-md border"
        >
          <table className="w-full min-w-[40rem] text-left text-sm">
            <caption className="sr-only">Campanhas</caption>
            <thead className="bg-surface border-line text-ink-soft border-b">
              <tr>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Campanha
                </th>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Período
                </th>
                <th scope="col" className="px-3 py-2.5 text-right font-semibold">
                  Desconto
                </th>
                <th scope="col" className="px-3 py-2.5 text-right font-semibold">
                  Produtos
                </th>
                <th scope="col" className="px-3 py-2.5 font-semibold">
                  Situação
                </th>
              </tr>
            </thead>
            <tbody className="divide-line divide-y">
              {result.data.data.map((c) => (
                <tr key={c.id} className="hover:bg-ink/4">
                  <td className="px-3 py-2.5">
                    <Link href={`/admin/campanhas/${c.id}`} className="font-medium hover:underline">
                      {c.name}
                    </Link>
                  </td>
                  <td className="text-ink-soft px-3 py-2.5">{formatPeriod(c)}</td>
                  <td className="font-condensed px-3 py-2.5 text-right text-base font-bold">
                    {c.discount_percent}%
                  </td>
                  <td className="font-condensed px-3 py-2.5 text-right text-base font-bold">
                    {c.product_count ?? "–"}
                  </td>
                  <td className="px-3 py-2.5">
                    <CampaignPhase campaign={c} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
      {result.ok && result.data.page.has_more && result.data.page.next_cursor && (
        <Link
          href={`/admin/campanhas?cursor=${encodeURIComponent(result.data.page.next_cursor)}`}
          className={buttonVariants({ variant: "secondary" })}
        >
          Campanhas mais antigas
        </Link>
      )}
    </div>
  );
}
