import { brand } from "@/lib/brand";
import { cn } from "@/lib/utils";

/**
 * O símbolo é a caixa de presente de Natal (o nome vem de "embrulhar"): caixa vermelha, fita e laço dourados.
 * Cores fixas, as da marca, nos dois temas; o mesmo desenho do favicon (app/icon.svg). Ao lado, o nome em texto,
 * de lib/brand.ts.
 */
export function LogoMark({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 32 32" aria-hidden="true" className={cn("size-8", className)}>
      <rect x="6" y="15" width="20" height="13.5" rx="1.2" fill="#c8202f" />
      <rect x="6" y="15" width="20" height="2.2" fill="#a51a27" />
      <rect x="4" y="10.5" width="24" height="5.5" rx="1.2" fill="#e1313f" />
      <rect x="14.25" y="10.5" width="3.5" height="18" fill="#f2b632" />
      <path d="M16 10.5c-3-4.6-8.7-5.6-9-2s5.4 3 9 2zm0 0c3-4.6 8.7-5.6 9-2s-5.4 3-9 2z" fill="#f2b632" />
      <circle cx="16" cy="10.5" r="1.9" fill="#b8860b" />
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
