import { CircleCheck, RefreshCw } from "lucide-react";
import type { Metadata } from "next";
import { PricingRuleForm } from "@/components/admin/pricing-rule-form";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { listCategories, pricingSettings, type PricingSettings } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { formatMoney } from "@/lib/format";
import { parseRate } from "@/lib/pricing";
import { categoryOptions } from "../produtos/category-options";
import { removePricingRule, syncFeed } from "./actions";

export const metadata: Metadata = { title: "Fornecedor" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

const plural = (n: number, one: string, many: string) => `${n} ${n === 1 ? one : many}`;
const brl = (cents: number) => formatMoney({ amount_cents: cents, currency: "BRL" });

/** O que a página conta depois de uma ação (a query que a ação deixou). */
function notice(q: Record<string, string | string[] | undefined>): { ok: boolean; text: string } | undefined {
  const n = (k: string) => Number(q[k] ?? 0);
  if (q.feed === "ok") {
    const changed = n("novos") + n("alterados") + n("fora");
    return {
      ok: true,
      text:
        changed === 0
          ? `Feed lido: nada mudou desde a última leitura (${plural(n("iguais"), "produto", "produtos")}).`
          : `Feed lido: ${plural(n("novos"), "novo", "novos")}, ${plural(n("alterados"), "alterado", "alterados")} e ${plural(n("fora"), "saiu", "saíram")} do feed. A loja atualiza em segundos.`,
    };
  }
  if (q.feed === "erro") {
    return {
      ok: false,
      text:
        q.status === "409"
          ? "Uma leitura do feed já está em andamento. Espere um pouco e veja de novo."
          : `O fornecedor não respondeu (${q.status}). Nada mudou na loja; tente de novo.`,
    };
  }
  if (typeof q.regra === "string" || q.removida === "1") {
    const what = q.removida === "1" ? "Regra removida" : "Regra gravada";
    return {
      ok: true,
      text: `${what}: ${plural(n("reprecificados"), "produto mudou", "produtos mudaram")} de preço.`,
    };
  }
  if (typeof q.erro === "string")
    return { ok: false, text: `A regra não foi removida (${q.erro}). Tente de novo.` };
}

/**
 * O catálogo do fornecedor de dropshipping (#217): a margem que transforma o custo em preço (a padrão e a
 * de cada categoria), o câmbio, e a leitura do feed sob demanda (o fulfillment lê sozinho de hora em hora).
 */
export default async function Supplier({ searchParams }: Props) {
  const { token } = await requireAdmin("/admin/fornecedor");
  const [result, categories, tree, query] = await Promise.all([
    pricingSettings(token),
    listCategories(),
    categoryOptions(),
    searchParams,
  ]);
  const message = notice(query);
  const names = new Map((categories.ok ? categories.data.data : []).map((c) => [c.id, c.name]));

  return (
    <div className="max-w-5xl space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Fornecedor</h1>
          <p className="text-ink-soft max-w-prose">
            Os produtos do fornecedor entram na loja com o preço pela margem abaixo, e o preço acompanha
            quando o custo muda. O feed é lido de hora em hora; leia agora para ver uma mudança já.
          </p>
        </div>
        <form action={syncFeed}>
          <SubmitButton variant="secondary" pendingLabel="Lendo o feed…">
            <RefreshCw aria-hidden="true" className="size-4" />
            Ler o feed agora
          </SubmitButton>
        </form>
      </div>

      {message && (
        <p
          role={message.ok ? "status" : "alert"}
          className={
            message.ok ? "text-success flex items-center gap-2 font-semibold" : "text-danger font-semibold"
          }
        >
          {message.ok && <CircleCheck aria-hidden="true" className="size-5 shrink-0" />}
          {message.text}
        </p>
      )}

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="As regras de margem não carregaram"
          description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
        />
      ) : (
        <SupplierRules settings={result.data} names={names} tree={tree} />
      )}
    </div>
  );
}

function SupplierRules({
  settings,
  names,
  tree,
}: {
  settings: PricingSettings;
  names: Map<string, string>;
  tree: Awaited<ReturnType<typeof categoryOptions>>;
}) {
  const usd = settings.exchange_rates.USD ?? "";
  const rateMilli = parseRate(usd);
  if (!rateMilli) {
    return (
      <StateMessage
        tone="error"
        title="Sem câmbio do dólar"
        description="O catálogo não informou o câmbio (supplier.usd_brl). Sem ele, nenhum preço é calculado."
      />
    );
  }
  const d = settings.default;
  return (
    <div className="grid gap-10 lg:grid-cols-2">
      <section aria-labelledby="padrao" className="space-y-4">
        <div className="space-y-1">
          <h2 id="padrao" className="text-xl font-semibold tracking-tight">
            Margem padrão
          </h2>
          <p className="text-ink-soft text-sm">
            Vale para toda categoria sem regra própria. Câmbio: US$ 1 = R$ {usd.replace(".", ",")}, da
            configuração do serviço.
            {!d.updated_at && " Ainda é a da configuração; gravar aqui passa a valer a sua."}
          </p>
        </div>
        <PricingRuleForm
          key={`padrao-${d.margin_percent}-${d.min_price_cents}`}
          rateMilli={rateMilli}
          initial={d}
          submitLabel="Gravar a margem padrão"
        />
      </section>

      <section aria-labelledby="categorias" className="space-y-4">
        <div className="space-y-1">
          <h2 id="categorias" className="text-xl font-semibold tracking-tight">
            Por categoria
          </h2>
          <p className="text-ink-soft text-sm">
            Para margens diferentes por categoria (beleza costuma aguentar mais que eletrônicos). Gravar ou
            remover reprecifica os produtos do fornecedor na hora.
          </p>
        </div>
        {settings.rules.length > 0 ? (
          <ul className="divide-line border-line divide-y rounded-md border">
            {settings.rules.map((r) => (
              <li key={r.category_id} className="flex flex-wrap items-center justify-between gap-3 px-4 py-3">
                <div>
                  <p className="font-semibold">{names.get(r.category_id ?? "") ?? "Categoria removida"}</p>
                  <p className="text-ink-soft text-sm tabular-nums">
                    {r.margin_percent}% de margem, mínimo {brl(r.min_price_cents)}
                  </p>
                </div>
                <form action={removePricingRule}>
                  <input type="hidden" name="category_id" value={r.category_id} />
                  <SubmitButton variant="ghost" size="sm" pendingLabel="Removendo…">
                    Remover
                    <span className="sr-only"> a regra de {names.get(r.category_id ?? "")}</span>
                  </SubmitButton>
                </form>
              </li>
            ))}
          </ul>
        ) : (
          <p className="text-ink-soft text-sm">Nenhuma ainda: todas as categorias seguem a margem padrão.</p>
        )}
        <details className="border-line rounded-md border p-4" open={settings.rules.length === 0}>
          <summary className="cursor-pointer font-semibold">Nova regra de categoria</summary>
          <div className="mt-4">
            <PricingRuleForm
              rateMilli={rateMilli}
              initial={d}
              categories={tree}
              submitLabel="Gravar a regra da categoria"
            />
          </div>
        </details>
      </section>
    </div>
  );
}
