import { CircleCheck, TriangleAlert } from "lucide-react";
import type { Metadata } from "next";
import { StateMessage } from "@/components/ui/state";
import { SubmitButton } from "@/components/ui/submit-button";
import { DLT_SERVICES, deadLetters, type DeadLetter } from "@/lib/admin-api";
import { requireAdmin } from "@/lib/auth/current";
import { replay } from "./actions";

export const metadata: Metadata = { title: "Mensagens com falha" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

const when = new Intl.DateTimeFormat("pt-BR", {
  day: "numeric",
  month: "short",
  hour: "2-digit",
  minute: "2-digit",
  second: "2-digit",
  timeZone: "America/Sao_Paulo",
});

/**
 * As mensagens que cada serviço não conseguiu processar em todas as tentativas (ou que nem eram um
 * evento válido) e o reprocessamento delas, depois de corrigida a causa (ADR 0003).
 */
export default async function DeadLetters({ searchParams }: Props) {
  const query = await searchParams;
  const { token } = await requireAdmin("/admin/falhas");
  const results = await Promise.all(DLT_SERVICES.map((s) => deadLetters(token, s.key)));
  const service = DLT_SERVICES.find((s) => s.key === query.servico);
  const total = results.reduce((n, r) => n + (r.ok ? r.data.items.length : 0), 0);

  return (
    <div className="max-w-5xl space-y-6">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Mensagens com falha</h1>
        <p className="text-ink-soft max-w-prose">
          Eventos que um serviço não conseguiu processar depois de todas as tentativas. Corrija a causa (o
          erro de cada uma diz qual foi) e reprocesse: o que já tinha sido feito não se repete.
        </p>
      </div>
      {service && typeof query.reprocessadas === "string" && (
        <p role="status" className="text-success flex items-center gap-2 font-semibold">
          <CircleCheck aria-hidden="true" className="size-5" />
          {query.reprocessadas === "1"
            ? "1 mensagem devolvida"
            : `${query.reprocessadas} mensagens devolvidas`}{" "}
          ao serviço de {service.label.toLowerCase()}. Se falharem de novo, voltam para cá.
        </p>
      )}
      {service && typeof query.erro === "string" && (
        <p role="alert" className="text-danger font-semibold">
          O reprocessamento em {service.label.toLowerCase()} não aconteceu ({query.erro}). Tente de novo.
        </p>
      )}
      {total === 0 && results.every((r) => r.ok) && (
        <StateMessage
          title="Nenhuma mensagem com falha"
          description="Todos os serviços processaram os eventos que receberam."
        />
      )}

      {DLT_SERVICES.map((s, i) => {
        const r = results[i]!;
        if (r.ok && r.data.items.length === 0) return null;
        return (
          <section key={s.key} aria-labelledby={`dlt-${s.key}`} className="space-y-3">
            <div className="flex flex-wrap items-center justify-between gap-3">
              <div>
                <h2 id={`dlt-${s.key}`} className="text-xl font-semibold tracking-tight">
                  {s.label}
                  {r.ok && (
                    <span className="font-condensed text-danger ml-2 tabular-nums">
                      <span className="sr-only">: </span>
                      {r.data.items.length}
                      <span className="sr-only"> {r.data.items.length === 1 ? "mensagem" : "mensagens"}</span>
                    </span>
                  )}
                </h2>
                <p className="text-ink-soft text-sm">Recebe {s.consumes}.</p>
              </div>
              {r.ok && (
                <form action={replay}>
                  <input type="hidden" name="service" value={s.key} />
                  <SubmitButton variant="secondary" size="sm" pendingLabel="Reprocessando…">
                    Reprocessar {r.data.items.length === 1 ? "a mensagem" : `as ${r.data.items.length}`}
                  </SubmitButton>
                </form>
              )}
            </div>
            {!r.ok ? (
              <p role="alert" className="text-danger flex items-center gap-2 text-sm font-semibold">
                <TriangleAlert aria-hidden="true" className="size-4" />
                {r.error.status === 503
                  ? "O Kafka não respondeu: não dá para ler a fila agora."
                  : `O serviço não respondeu (${r.error.status}).`}
              </p>
            ) : (
              <ul className="divide-line border-line divide-y rounded-md border">
                {r.data.items.map((m, j) => (
                  <DeadLetterItem key={m.event_id ?? `${m.topic}-${m.offset}-${j}`} message={m} />
                ))}
              </ul>
            )}
          </section>
        );
      })}
    </div>
  );
}

function DeadLetterItem({ message: m }: { message: DeadLetter }) {
  return (
    <li className="space-y-1 px-4 py-3 text-sm">
      <div className="flex flex-wrap items-baseline justify-between gap-x-4">
        <p className="font-semibold">{m.event_type ?? "Mensagem que não é um evento válido"}</p>
        {m.failed_at && (
          <time dateTime={m.failed_at} className="text-ink-soft tabular-nums">
            {when.format(new Date(m.failed_at))}
          </time>
        )}
      </div>
      <p className="text-ink-soft">
        {m.topic}, partição {m.partition ?? "?"}, posição {m.offset ?? "?"};{" "}
        {m.attempts === 1 ? "1 tentativa" : `${m.attempts} tentativas`}
        {m.event_id && <>; evento {m.event_id}</>}
      </p>
      {m.error && (
        <details>
          <summary className="cursor-pointer font-medium">Erro</summary>
          <pre className="bg-ink/6 mt-1 overflow-x-auto rounded-sm p-2 text-xs whitespace-pre-wrap">
            {m.error}
          </pre>
        </details>
      )}
    </li>
  );
}
