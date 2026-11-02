import Link from "next/link";
import type { AdminResult, SupplierStatus } from "@/lib/admin-api";
import { deadline } from "@/lib/overdue";
import { cn } from "@/lib/utils";

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});
const moment = (iso: string | null | undefined, never: string) => (iso ? when.format(new Date(iso)) : never);
const count = new Intl.NumberFormat("pt-BR");

/**
 * A integração com o fornecedor (#225): se ele responde agora, o que a loja recebeu dele e os prazos em vigor. As
 * credenciais e os prazos ficam na configuração do ambiente: a tela conta como estão, sem mostrar segredo.
 */
export function SupplierStatusPanel({ status }: { status: AdminResult<SupplierStatus> }) {
  if (!status.ok) {
    return (
      <p className="text-danger font-medium">
        O status do fornecedor não carregou ({status.error.status}).{" "}
        <Link href="/admin/fornecedor" className="text-ink underline underline-offset-4">
          Tentar de novo
        </Link>
      </p>
    );
  }
  const s = status.data;
  // a terceira coluna marca o que impede vender: aparece como problema, e não como mais uma linha (#273)
  const rows: [string, string, boolean?][] = [
    ["Endereço", s.host],
    [
      "Credenciais",
      s.own_credentials
        ? "Guardadas nos segredos do ambiente"
        : "As do simulador: troque nos segredos do ambiente antes de vender",
      !s.own_credentials,
    ],
    [
      "Webhook",
      s.webhook_secret_configured
        ? `Assinado; último em ${moment(s.last_webhook_at, "nenhum ainda")}`
        : "Sem segredo: os avisos do fornecedor são recusados",
      !s.webhook_secret_configured,
    ],
    ["Produtos no feed", count.format(s.products_in_feed)],
    ["Último estoque lido", moment(s.last_stock_read_at, "nenhum ainda")],
    [
      "Prazos",
      `Despacho em ${deadline(s.ship_within_hours)}, entrega em ${deadline(s.deliver_within_hours)}, resposta à devolução em ${deadline(s.return_within_hours)}`,
    ],
  ];
  return (
    <section aria-labelledby="integracao" className="space-y-3">
      <div className="flex flex-wrap items-baseline gap-x-3">
        <h2 id="integracao" className="text-xl font-semibold tracking-tight">
          Integração com {s.name.toUpperCase()}
        </h2>
        <p className={cn("font-semibold tabular-nums", s.reachable ? "text-success" : "text-danger")}>
          {s.reachable ? `Respondendo (${s.latency_ms} ms)` : "Fora do ar"}
        </p>
      </div>
      {s.error && <p className="text-danger text-sm [overflow-wrap:anywhere]">{s.error}</p>}
      <dl className="divide-line border-line divide-y rounded-md border text-sm">
        {rows.map(([term, value, blocking]) => (
          <div key={term} className="grid gap-x-4 px-4 py-2 sm:grid-cols-[12rem_1fr]">
            <dt className="text-ink-soft">{term}</dt>
            {/* o endereço e o erro vêm sem espaço para quebrar: no celular, estouravam a coluna */}
            <dd className={cn("[overflow-wrap:anywhere]", blocking && "text-danger font-semibold")}>
              {blocking && <span className="sr-only">Pendente: </span>}
              {value}
            </dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
