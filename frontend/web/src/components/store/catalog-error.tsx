import Link from "next/link";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import type { CatalogError } from "@/lib/catalog-api";

/** O catálogo não respondeu: diz o porquê (limite de requisições ou fora do ar) e oferece tentar de novo. */
export function CatalogErrorState({
  error,
  retryHref,
  headingLevel = 2,
}: {
  error: CatalogError;
  retryHref: string;
  headingLevel?: 2 | 3;
}) {
  const limited = error.status === 429;
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
