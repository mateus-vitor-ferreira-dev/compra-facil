"use client";

import Link from "next/link";
import { usePathname } from "next/navigation";
import { cn } from "@/lib/utils";

export const ADMIN_SECTIONS = [
  { href: "/admin/produtos", label: "Produtos" },
  { href: "/admin/categorias", label: "Categorias" },
  { href: "/admin/campanhas", label: "Campanhas" },
  { href: "/admin/atrasos", label: "Pedidos atrasados" },
  { href: "/admin/falhas", label: "Mensagens com falha" },
] as const;

/** As seções do painel; a atual fica marcada (aria-current), por baixo da barra. */
export function AdminNav() {
  const path = usePathname();
  return (
    <nav aria-label="Seções do painel" className="-mb-px flex gap-1 overflow-x-auto">
      {ADMIN_SECTIONS.map((s) => {
        const current = path === s.href || path.startsWith(`${s.href}/`);
        return (
          <Link
            key={s.href}
            href={s.href}
            aria-current={current ? "page" : undefined}
            className={cn(
              "text-on-hero/75 hover:text-on-hero inline-flex min-h-11 items-center border-b-2 border-transparent px-3 font-medium whitespace-nowrap",
              current && "border-tag text-on-hero",
            )}
          >
            {s.label}
          </Link>
        );
      })}
    </nav>
  );
}
