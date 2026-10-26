import { Check } from "lucide-react";
import Link from "next/link";
import { cn } from "@/lib/utils";

/**
 * Um filtro da listagem como link (funciona sem JavaScript e fica na URL). Ligado, ganha o visto e o
 * fundo em tinta; o leitor de tela ouve o estado.
 */
export function FilterChip({
  href,
  active,
  children,
}: {
  href: string;
  active: boolean;
  children: React.ReactNode;
}) {
  return (
    <Link
      href={href}
      scroll={false}
      aria-current={active ? "true" : undefined}
      className={cn(
        "inline-flex min-h-11 items-center gap-1.5 rounded-full border px-4 text-sm font-medium sm:min-h-9",
        active ? "bg-ink text-paper border-ink" : "border-ink/25 text-ink hover:border-ink/60",
      )}
    >
      {active && <Check aria-hidden="true" className="size-4" />}
      {children}
      {active && <span className="sr-only"> (ligado)</span>}
    </Link>
  );
}
