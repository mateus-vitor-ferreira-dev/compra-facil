import type { Metadata } from "next";
import { requireAdmin } from "@/lib/auth/current";
import { RegenerateForm } from "./regenerate-form";

export const metadata: Metadata = { title: "Segurança da conta" };

/**
 * A verificação em duas etapas da conta da equipe (ameaça T8): sempre ativa (o painel não abre sem ela), quantos
 * códigos de recuperação ainda valem e a troca deles, com um código atual do app.
 */
export default async function SecurityPage() {
  const { account } = await requireAdmin("/admin/seguranca");
  const left = account.recovery_codes_left ?? 0;

  return (
    <div className="max-w-3xl space-y-8">
      <div className="space-y-1">
        <h1 className="text-2xl font-semibold tracking-tight">Segurança da conta</h1>
        <p className="text-ink-soft max-w-prose">
          O painel pede a senha e o código do app autenticador. Os códigos de recuperação servem para entrar
          sem o celular, uma vez cada.
        </p>
      </div>

      <section aria-labelledby="duas-etapas" className="space-y-2">
        <h2 id="duas-etapas" className="text-xl font-semibold tracking-tight">
          Verificação em duas etapas
        </h2>
        <dl className="grid grid-cols-[auto_1fr] gap-x-6 gap-y-1">
          <dt className="text-ink-soft">Autenticação em duas etapas</dt>
          <dd className="font-medium">{account.two_factor_enabled ? "ativa" : "desligada"}</dd>
          <dt className="text-ink-soft">Códigos de recuperação</dt>
          <dd className="font-medium">
            {left === 1 ? "1 ainda vale" : `${left} ainda valem`}
            {left <= 3 && <span className="text-danger"> · gere códigos novos</span>}
          </dd>
        </dl>
      </section>

      <section aria-labelledby="novos-codigos" className="space-y-3">
        <h2 id="novos-codigos" className="text-xl font-semibold tracking-tight">
          Gerar códigos de recuperação novos
        </h2>
        <p className="text-ink-soft max-w-prose">
          Perdeu os códigos ou já usou muitos? Gere outros com o código que o app autenticador mostra agora.
        </p>
        <RegenerateForm />
      </section>
    </div>
  );
}
