import type { HTMLAttributes } from "react";
import { cn } from "@/lib/utils";

/** Bloco de carregamento no formato do que vem. Sem animação com prefers-reduced-motion (globals.css). */
export function Skeleton({ className, ...props }: HTMLAttributes<HTMLDivElement>) {
  return <div aria-hidden="true" className={cn("bg-photo animate-pulse rounded-md", className)} {...props} />;
}
