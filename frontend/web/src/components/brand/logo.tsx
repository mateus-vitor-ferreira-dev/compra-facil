import { brand } from "@/lib/brand";
import { cn } from "@/lib/utils";

/**
 * O símbolo é a caixa de presente (o nome vem de "embrulhar"), nas cores da CompraFácil: a caixa na cor da tinta
 * (currentColor, segue o tema, como a etiqueta antiga), a fita e o laço no amarelo da etiqueta. A linha de papel
 * separa a tampa da caixa. O mesmo desenho do favicon (app/icon.svg). Ao lado, o nome em texto, de lib/brand.ts.
 */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={cn("size-8", className)}>
      <rect x="6" y="15" width="20" height="13.5" rx="1.2" fill="currentColor" />
      <rect x="4" y="10.5" width="24" height="5.5" rx="1.2" fill="currentColor" />
      <rect x="6" y="16" width="20" height="1.4" className="fill-paper" />
      <rect x="14.25" y="10.5" width="3.5" height="18" className="fill-ribbon" />
      <path
        d="M16 10.5c-3-4.6-8.7-5.6-9-2s5.4 3 9 2zm0 0c3-4.6 8.7-5.6 9-2s-5.4 3-9 2z"
        className="fill-ribbon"
      />
      <circle cx="16" cy="10.5" r="1.9" fill="currentColor" />
    </svg>
  );
}

export function Logo({ className }: { className?: string }) {
  return (
    <span className={cn("text-ink inline-flex items-center gap-2", className)}>
      <LogoMark />
      <span className="text-lg font-bold tracking-tight">{brand.name}</span>
    </span>
  );
}
