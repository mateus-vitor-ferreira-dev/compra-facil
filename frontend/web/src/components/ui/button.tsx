import { cva, type VariantProps } from "class-variance-authority";
import type { ButtonHTMLAttributes } from "react";
import { cn } from "@/lib/utils";

/** Ações em tinta sólida: o amarelo fica só para o preço de campanha. */
export const buttonVariants = cva(
  "inline-flex cursor-pointer items-center justify-center gap-2 rounded-md font-semibold whitespace-nowrap transition-[color,background-color,border-color,transform] duration-150 active:translate-y-px disabled:pointer-events-none disabled:opacity-50 [&_svg]:size-4 [&_svg]:shrink-0",
  {
    variants: {
      variant: {
        primary: "bg-ink text-paper hover:bg-ink/85",
        secondary: "border border-ink/25 bg-surface text-ink hover:border-ink/60",
        ghost: "text-ink hover:bg-ink/8",
        danger: "bg-danger text-on-danger hover:bg-danger/85",
      },
      size: {
        // em tela de toque, todo botão tem pelo menos 44 px (o "sm" encolhe só com mouse)
        sm: "h-9 px-3 text-sm pointer-coarse:h-11",
        md: "h-11 px-5 text-base",
        icon: "size-11",
      },
    },
    defaultVariants: { variant: "primary", size: "md" },
  },
);

export type ButtonProps = ButtonHTMLAttributes<HTMLButtonElement> & VariantProps<typeof buttonVariants>;

export function Button({ className, variant, size, type = "button", ...props }: ButtonProps) {
  return <button type={type} className={cn(buttonVariants({ variant, size }), className)} {...props} />;
}
