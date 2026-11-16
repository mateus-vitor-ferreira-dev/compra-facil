import { CircleCheck, Download, RefreshCw } from "lucide-react";
import type { Metadata } from "next";
import { PricingRuleForm } from "@/components/admin/pricing-rule-form";
import { SupplierSheetForm } from "@/components/admin/supplier-sheet-form";
import { SupplierStatusPanel } from "@/components/admin/supplier-status";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import {
  listCategories,
  pricingSettings,
  supplierSheet,
  supplierStatus,
  type ManualSheetSummary,
  type PricingSettings,
} from "@/lib/admin-api";
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
  const [status, sheet, result, categories, tree, query] = await Promise.all([
    supplierStatus(token),
    supplierSheet(token),
    pricingSettings(token),
    listCategories(),
    categoryOptions(),
    searchParams,
  ]);
  const message = notice(query);
  const names = new Map((categories.ok ? categories.data.data : []).map((c) => [c.id, c.name]));
  const manual = sheet.result.ok && sheet.result.data.enabled ? sheet.result.data : null;

  return (
    <div className="max-w-5xl space-y-8">
      <div className="flex flex-wrap items-start justify-between gap-4">
        <div className="space-y-1">
          <h1 className="text-2xl font-semibold tracking-tight">Fornecedor</h1>
          <p className="text-ink-soft max-w-prose">
            Os produtos do fornecedor entram na loja com o preço pela margem abaixo, e o preço acompanha
            quando o custo muda.{" "}
            {manual
              ? "O catálogo e o estoque vêm da planilha que a equipe sobe aqui."
              : "O feed é lido de hora em hora; leia agora para ver uma mudança já."}
          </p>
        </div>
        {!manual && (
          <form action={syncFeed}>
            <SubmitButton variant="secondary" pendingLabel="Lendo o feed…">
              <RefreshCw aria-hidden="true" className="size-4" />
              Ler o feed agora
            </SubmitButton>
          </form>
        )}
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

      {manual ? <SheetSection sheet={manual} now={sheet.readAt} /> : <SupplierStatusPanel status={status} />}

      {!result.ok ? (
        <StateMessage
          tone="error"
          title="As regras de margem não carregaram"
          description={`O catálogo respondeu ${result.error.status}. Tente de novo em instantes.`}
        />
      ) : (
        <SupplierRules settings={result.data} names={names} tree={tree} manual={manual !== null} />
      )}
    </div>
  );
}

const when = new Intl.DateTimeFormat("pt-BR", {
  dateStyle: "short",
  timeStyle: "short",
  timeZone: "America/Sao_Paulo",
});

/** O fornecedor manual (#412): a planilha é o catálogo e o estoque dele. */
function SheetSection({ sheet, now }: { sheet: ManualSheetSummary; now: number }) {
  const days = sheet.imported_at ? (now - new Date(sheet.imported_at).getTime()) / 86_400_000 : null;
  return (
    <section aria-labelledby="planilha-titulo" className="border-line space-y-4 border-b pb-8">
      <div className="flex flex-wrap items-end justify-between gap-3">
        <div className="space-y-1">
          <h2 id="planilha-titulo" className="text-xl font-semibold tracking-tight">
            Planilha do fornecedor
          </h2>
          <p className="text-ink-soft max-w-prose text-sm">
            {sheet.imported_at ? (
              <>
                {plural(sheet.products, "produto", "produtos")}, subida em{" "}
                <time dateTime={sheet.imported_at}>{when.format(new Date(sheet.imported_at))}</time>.
                {days !== null && days > 3 && (
                  <span className="text-danger font-semibold">
                    {" "}
                    Faz mais de 3 dias: o estoque pode estar velho, e a loja vender o que o fornecedor não
                    tem.
                  </span>
                )}
              </>
            ) : (
              "Nenhuma planilha ainda. Suba a do fornecedor para os produtos entrarem na loja."
            )}
          </p>
        </div>
        <a
          href="/admin/fornecedor/modelo"
          download
          className="inline-flex min-h-11 items-center gap-2 font-medium underline underline-offset-4 pointer-fine:min-h-8"
        >
          <Download aria-hidden="true" className="size-4" />
          Baixar o modelo
        </a>
      </div>
      <p className="text-ink-soft max-w-prose text-sm">
        Colunas: <strong className="text-ink">sku</strong>, <strong className="text-ink">nome</strong>,{" "}
        <strong className="text-ink">categoria</strong> (o endereço da categoria na loja, como
        fones-over-ear), <strong className="text-ink">custo</strong> em reais,{" "}
        <strong className="text-ink">estoque</strong>, e, se tiver, peso_g e foto (um link https). A loja
        vende o estoque menos 1 unidade, de segurança.
      </p>
      <SupplierSheetForm />
    </section>
  );
}

function SupplierRules({
  settings,
  names,
  tree,
  manual,
}: {
  settings: PricingSettings;
  names: Map<string, string>;
  tree: Awaited<ReturnType<typeof categoryOptions>>;
  manual: boolean;
}) {
  const usd = settings.exchange_rates.USD ?? "";
  // no fornecedor manual, o custo da planilha já é em reais: a conta não passa pelo câmbio
  const rateMilli = manual ? 1000 : parseRate(usd);
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
            Vale para toda categoria sem regra própria.
            {!manual && <> Câmbio: US$ 1 = R$ {usd.replace(".", ",")}, da configuração do serviço.</>}
            {!d.updated_at && " Ainda é a da configuração; gravar aqui passa a valer a sua."}
          </p>
        </div>
        <PricingRuleForm
          key={`padrao-${d.margin_percent}-${d.min_price_cents}`}
          rateMilli={rateMilli}
          costInReais={manual}
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
                {/* remover reprecifica na hora os produtos da categoria: pede confirmação, como remover produto */}
                <details className="shrink-0">
                  <summary className="text-danger inline-flex min-h-11 cursor-pointer items-center font-semibold pointer-fine:min-h-8">
                    Remover
                    <span className="sr-only">
                      {" "}
                      a regra de {names.get(r.category_id ?? "") ?? "uma categoria removida"}
                    </span>
                  </summary>
                  <form action={removePricingRule} className="mt-2 max-w-xs space-y-2">
                    <input type="hidden" name="category_id" value={r.category_id} />
                    <p className="text-ink-soft text-sm">
                      Os produtos desta categoria voltam à margem padrão do fornecedor, e o preço muda na
                      vitrine na hora.
                    </p>
                    <SubmitButton variant="danger" size="sm" pendingLabel="Removendo…">
                      Remover a regra
                    </SubmitButton>
                  </form>
                </details>
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
              costInReais={manual}
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
