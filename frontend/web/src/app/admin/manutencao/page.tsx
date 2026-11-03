import type { Metadata } from "next";
import { MaintenanceForm } from "@/components/admin/maintenance-form";
import { SubmitButton } from "@/components/ui/submit-button";
import { requireAdmin } from "@/lib/auth/current";
import { toLocalInput } from "@/lib/campaign-time";
import { readMaintenance } from "@/lib/maintenance";
import { windowLabel } from "@/lib/maintenance-text";
import { cancelMaintenance } from "./actions";

export const metadata: Metadata = { title: "Manutenção" };

type Props = { searchParams: Promise<Record<string, string | string[] | undefined>> };

/**
 * A manutenção da loja (#370): a janela agendada, que a loja anuncia na faixa do topo, e o estado do deploy, que o
 * CI liga sozinho em cada release. A faixa aparece em até meio minuto (o cache do Cloudflare KV).
 */
export default async function MaintenancePage({ searchParams }: Props) {
  await requireAdmin("/admin/manutencao");
  const [{ deploying, schedule }, query] = await Promise.all([readMaintenance(), searchParams]);
  const notice = query.agendada
    ? "Manutenção agendada."
    : query.cancelada
      ? "Manutenção cancelada."
      : undefined;

  return (
    <div className="max-w-5xl space-y-8">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Manutenção</h1>
        <p className="text-ink-soft max-w-prose">
          Agende uma janela para a loja avisar quem compra com antecedência. Nos releases, o aviso de
          atualização aparece sozinho durante o deploy da API.
        </p>
      </div>

      {notice && (
        <p role="status" className="border-success bg-surface rounded-md border-l-4 px-4 py-3">
          {notice} A loja mostra a mudança em até meio minuto.
        </p>
      )}

      <section aria-labelledby="agora" className="space-y-2">
        <h2 id="agora" className="text-xl font-semibold tracking-tight">
          Agora
        </h2>
        <p>
          {deploying
            ? "Um deploy da API está em andamento: a loja avisa que está sendo atualizada e segura os pedidos."
            : "Nenhum deploy em andamento."}
        </p>
        {schedule ? (
          <div className="border-line bg-surface max-w-xl space-y-3 rounded-md border p-4">
            <p>
              <span className="font-semibold">
                {schedule.phase === "now" ? "Em andamento" : "Agendada"}:{" "}
                {windowLabel(schedule.starts_at, schedule.ends_at)}
              </span>
              {schedule.message && <span className="text-ink-soft block text-sm">{schedule.message}</span>}
            </p>
            <form action={cancelMaintenance}>
              <SubmitButton variant="secondary" size="sm" pendingLabel="Cancelando…">
                Cancelar a manutenção
              </SubmitButton>
            </form>
          </div>
        ) : (
          <p className="text-ink-soft">Nenhuma manutenção agendada.</p>
        )}
      </section>

      <section aria-labelledby="agendar" className="space-y-3">
        <h2 id="agendar" className="text-xl font-semibold tracking-tight">
          {schedule ? "Trocar a janela" : "Agendar"}
        </h2>
        <MaintenanceForm
          initial={{
            starts: schedule ? toLocalInput(schedule.starts_at) : "",
            ends: schedule ? toLocalInput(schedule.ends_at) : "",
            message: schedule?.message ?? "",
          }}
        />
      </section>
    </div>
  );
}
