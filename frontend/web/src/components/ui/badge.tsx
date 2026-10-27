import { cva, type VariantProps } from "class-variance-authority";
import type { HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

/**
 * Selos curtos. "deal" é o único amarelo fora do preço: o desconto da campanha. Os de status do pedido
 * levam ícone ou texto, nunca só a cor.
 */
const badgeVariants = cva("inline-flex items-center gap-1 rounded-sm px-2 py-0.5 text-xs font-semibold", {
  variants: {
    variant: {
      deal: "bg-tag text-tag-ink",
      neutral: "bg-ink/8 text-ink",
      success: "bg-success/12 text-success",
      danger: "bg-danger/12 text-danger",
      outline: "border border-line text-ink-soft",
    },
  },
  defaultVariants: { variant: "neutral" },
});

export type BadgeProps = HTMLAttributes<HTMLSpanElement> & VariantProps<typeof badgeVariants>;

export function Badge({ className, variant, ...props }: BadgeProps) {
  return <span className={cn(badgeVariants({ variant }), className)} {...props} />;
}
