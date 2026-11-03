import Link from "next/link";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { AutoRefresh } from "@/components/ui/auto-refresh";
import { StateMessage } from "@/components/ui/state";
import type { CatalogError } from "@/lib/catalog-api";
import { readMaintenance } from "@/lib/maintenance";

/**
 * O catálogo não respondeu: diz o porquê (limite de requisições, loja sendo atualizada ou fora do ar) e oferece
 * tentar de novo. Durante o deploy (#370), não é um erro: a página avisa e tenta de novo sozinha.
 */
export async function CatalogErrorState({
  error,
  retryHref,
  headingLevel = 2,
}: {
  error: CatalogError;
  retryHref: string;
  headingLevel?: 2 | 3;
}) {
  const limited = error.status === 429;
  if (!limited && (await readMaintenance()).deploying) {
    return (
      <>
        <AutoRefresh />
        <StateMessage
          headingLevel={headingLevel}
          illustration={<NotFoundIllustration />}
          title="Estamos atualizando a loja"
          description="Os produtos voltam em cerca de 1 minuto. Esta página tenta de novo sozinha."
          action={
            <Link href={retryHref} className={buttonVariants({ variant: "secondary" })}>
              Tentar agora
            </Link>
          }
        />
      </>
    );
  }
  return (
    <StateMessage
      tone="error"
      headingLevel={headingLevel}
      illustration={<NotFoundIllustration />}
      title={limited ? "Muitas buscas em pouco tempo" : "Os produtos não carregaram"}
      description={
        limited
          ? `A loja limita as consultas seguidas. Espere ${error.retryAfter ?? 30} segundos e tente de novo.`
          : "O catálogo não respondeu agora. Tente de novo em instantes."
      }
      action={
        <Link href={retryHref} className={buttonVariants({ variant: "secondary" })}>
          Tentar de novo
        </Link>
      }
    />
  );
}
