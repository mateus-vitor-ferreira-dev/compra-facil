import { cn } from "@/lib/utils";

/**
 * Ilustrações de estado em traço (currentColor), no mesmo desenho da etiqueta do logo. Decorativas:
 * o texto ao lado diz o que aconteceu.
 */
const base = "h-24 w-auto text-ink";

export function EmptyCartIllustration({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 120 96" fill="none" aria-hidden="true" className={cn(base, className)}>
      <path
        d="M14 18h12l10 46h56l10-32H32"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
      <circle cx="44" cy="78" r="6" stroke="currentColor" strokeWidth="3" />
      <circle cx="84" cy="78" r="6" stroke="currentColor" strokeWidth="3" />
      <path
        d="M60 30l-4-14 10-4"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
        strokeLinejoin="round"
        opacity=".35"
      />
    </svg>
  );
}

export function OrderConfirmedIllustration({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 120 96" fill="none" aria-hidden="true" className={cn(base, className)}>
      <path
        d="M22 30 60 12l38 18v38L60 86 22 68V30Z"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinejoin="round"
      />
      <path
        d="M22 30l38 18 38-18M60 48v38"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinejoin="round"
        opacity=".35"
      />
      <circle cx="90" cy="70" r="16" className="fill-tag" />
      <path
        d="m83 70 5 5 9-10"
        className="stroke-tag-ink"
        strokeWidth="3"
        strokeLinecap="round"
        strokeLinejoin="round"
      />
    </svg>
  );
}

export function NotFoundIllustration({ className }: { className?: string }) {
  return (
    <svg viewBox="0 0 120 96" fill="none" aria-hidden="true" className={cn(base, className)}>
      <path
        d="M30 26 40 16h52a4 4 0 0 1 4 4v52a4 4 0 0 1-4 4H34a4 4 0 0 1-4-4V26Z"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinejoin="round"
      />
      <circle cx="44" cy="30" r="4" stroke="currentColor" strokeWidth="3" />
      <path
        d="M50 50h26M50 62h14"
        stroke="currentColor"
        strokeWidth="3"
        strokeLinecap="round"
        opacity=".35"
      />
      <path d="m98 18 10-8M104 30h10" stroke="currentColor" strokeWidth="3" strokeLinecap="round" />
    </svg>
  );
}
