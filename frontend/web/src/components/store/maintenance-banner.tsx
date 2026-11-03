import { Wrench } from "lucide-react";
import type { Maintenance } from "@/lib/maintenance";
import { untilLabel, windowLabel } from "@/lib/maintenance-text";

/**
 * A faixa de manutenção (#370), no topo da loja. Durante o deploy, o ponto pulsando diz que é agora (parado com
 * movimento reduzido). Antes da janela agendada, avisa quando; durante ela, até quando. Sem manutenção, nada.
 */
export function MaintenanceBanner({ maintenance }: { maintenance: Maintenance }) {
  const { deploying, schedule } = maintenance;
  if (!deploying && !schedule) return null;

  const text = deploying
    ? "Estamos atualizando a loja. Leva cerca de 1 minuto, e o que não carregar volta sozinho."
    : schedule!.phase === "now"
      ? `Manutenção em andamento, ${untilLabel(schedule!.ends_at)}. A loja pode ficar fora do ar por alguns instantes.`
      : `Manutenção programada: ${windowLabel(schedule!.starts_at, schedule!.ends_at)}. A loja pode ficar fora do ar por alguns instantes nesse horário.`;

  return (
    <div role="status" className="bg-surface border-line text-ink border-b px-4 py-2 text-sm">
      <p className="mx-auto flex max-w-6xl items-start gap-2 sm:items-center">
        {deploying || schedule?.phase === "now" ? (
          <span aria-hidden="true" className="relative mt-1.5 flex size-2.5 shrink-0 sm:mt-0">
            <span className="bg-ink/40 absolute inline-flex size-full animate-ping rounded-full motion-reduce:animate-none" />
            <span className="bg-ink relative inline-flex size-2.5 rounded-full" />
          </span>
        ) : (
          <Wrench aria-hidden="true" className="text-ink-soft mt-0.5 size-4 shrink-0 sm:mt-0" />
        )}
        <span>
          <span className="font-semibold">{text}</span>
          {!deploying && schedule?.message && <span className="text-ink-soft"> {schedule.message}</span>}
        </span>
      </p>
    </div>
  );
}
