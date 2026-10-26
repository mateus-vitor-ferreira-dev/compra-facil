import Link from "next/link";
import { NotFoundIllustration } from "@/components/brand/illustrations";
import { buttonVariants } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";

export default function NotFound() {
  return (
    <main id="conteudo" className="mx-auto max-w-6xl px-4 pt-6 sm:px-6">
      <StateMessage
        illustration={<NotFoundIllustration />}
        title="Esta página não existe"
        description="O endereço pode ter mudado ou o produto saiu do catálogo. Busque pelo nome ou volte ao início."
        action={
          <Link href="/" className={buttonVariants()}>
            Ir para o início
          </Link>
        }
      />
    </main>
  );
}
