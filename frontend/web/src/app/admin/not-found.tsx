import Link from "next/link";
import { MissingTag } from "@/components/brand/missing-tag";
import { buttonVariants } from "@/components/ui/button";

/**
 * O que não existe no painel (um pedido ou produto de id errado, um endereço antigo): a etiqueta menor, e as
 * seções por onde a equipe costuma começar. O menu ao lado continua lá.
 */
export default function AdminNotFound() {
  return (
    <div className="flex flex-col items-start gap-8 py-6 sm:flex-row sm:items-center sm:gap-12">
      <MissingTag size="sm" className="self-center sm:order-last" />
      <div className="max-w-prose space-y-5">
        <div className="space-y-2">
          <h1 className="text-2xl font-semibold tracking-tight">Isto não existe no painel</h1>
          <p className="text-ink-soft">
            O pedido ou o produto pode ter outro número, ou o endereço mudou. Volte por uma das seções.
          </p>
        </div>
        <div className="flex flex-wrap gap-3">
          <Link href="/admin" className={buttonVariants()}>
            Ir para a visão geral
          </Link>
          <Link href="/admin/pedidos" className={buttonVariants({ variant: "secondary" })}>
            Ver os pedidos
          </Link>
          <Link href="/admin/produtos" className={buttonVariants({ variant: "secondary" })}>
            Ver os produtos
          </Link>
        </div>
      </div>
    </div>
  );
}
