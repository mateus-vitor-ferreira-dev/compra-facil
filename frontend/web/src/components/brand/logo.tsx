import { brand } from "@/lib/brand";
import { cn } from "@/lib/utils";

/**
 * O símbolo é a etiqueta de preço (o furo e o canto chanfrado), independente do nome; ao lado, o
 * nome em texto, de lib/brand.ts. currentColor: segue o tema.
 */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={cn("size-8", className)}>
      <path d="M4 9.5 9.5 4H26a2 2 0 0 1 2 2v20a2 2 0 0 1-2 2H6a2 2 0 0 1-2-2V9.5Z" fill="currentColor" />
      <circle cx="10" cy="10" r="2.25" className="fill-paper" />
      <path d="M11 21.5h10M11 17h6" className="stroke-paper" strokeWidth="2.5" strokeLinecap="round" />
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
