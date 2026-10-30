import type { AdminResult, SupplierStatus } from "@/lib/admin-api";
import { cn } from "@/lib/utils";

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  timeZone: "America/Sao_Paulo",
});
const moment = (iso: string | null | undefined, never: string) => (iso ? when.format(new Date(iso)) : never);
const span = (hours: number) => (hours >= 48 ? `${Math.round(hours / 24)} dias` : `${Math.round(hours)} h`);

/**
 * A integração com o fornecedor (#225): se ele responde agora, o que a loja recebeu dele e os prazos em vigor. As
 * credenciais e os prazos ficam na configuração do ambiente: a tela conta como estão, sem mostrar segredo.
 */
export function SupplierStatusPanel({ status }: { status: AdminResult<SupplierStatus> }) {
  if (!status.ok) {
    return (
      <p className="text-danger font-medium">
        O status do fornecedor não carregou ({status.error.status}). Tente de novo em instantes.
      </p>
    );
  }
  const s = status.data;
  const rows: [string, string][] = [
    ["Endereço", s.host],
    [
      "Credenciais",
      s.own_credentials
        ? "Guardadas nos segredos do ambiente"
        : "As do simulador: troque nos segredos do ambiente antes de vender",
    ],
    [
      "Webhook",
      s.webhook_secret_configured
        ? `Assinado; último em ${moment(s.last_webhook_at, "nenhum ainda")}`
        : "Sem segredo: os avisos do fornecedor são recusados",
    ],
    ["Produtos no feed", String(s.products_in_feed)],
    ["Último estoque lido", moment(s.last_stock_read_at, "nenhum ainda")],
    [
      "Prazos",
      `Despacho em ${span(s.ship_within_hours)}, entrega em ${span(s.deliver_within_hours)}, resposta à devolução em ${span(s.return_within_hours)}`,
    ],
  ];
  return (
    <section aria-labelledby="integracao" className="space-y-3">
      <div className="flex flex-wrap items-baseline gap-x-3">
        <h2 id="integracao" className="text-xl font-semibold tracking-tight">
          Integração com {s.name.toUpperCase()}
        </h2>
        <p className={cn("font-semibold", s.reachable ? "text-success" : "text-danger")}>
          {s.reachable ? `Respondendo (${s.latency_ms} ms)` : "Fora do ar"}
        </p>
      </div>
      {s.error && <p className="text-danger text-sm">{s.error}</p>}
      <dl className="divide-line border-line divide-y rounded-md border text-sm">
        {rows.map(([term, value]) => (
          <div key={term} className="grid gap-x-4 px-4 py-2 sm:grid-cols-[12rem_1fr]">
            <dt className="text-ink-soft">{term}</dt>
            <dd>{value}</dd>
          </div>
        ))}
      </dl>
    </section>
  );
}
