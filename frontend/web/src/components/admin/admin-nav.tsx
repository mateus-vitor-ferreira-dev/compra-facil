"use client";

import {
  CircleAlert,
  Factory,
  FolderTree,
  Menu,
  Package,
  Percent,
  ReceiptText,
  Tag,
  Truck,
  Undo2,
  Wrench,
  X,
  type LucideIcon,
} from "lucide-react";
import Link from "next/link";
import { usePathname } from "next/navigation";
import { useId, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

type Section = { href: string; label: string; Icon: LucideIcon; exact?: boolean };

/** As seções do painel, pelo que a equipe vai fazer: vender, cuidar do catálogo, operar e manter. */
export const ADMIN_GROUPS: { label: string; sections: Section[] }[] = [
  {
    label: "Vendas",
    sections: [
      { href: "/admin/pedidos", label: "Pedidos", Icon: ReceiptText },
      { href: "/admin/margens", label: "Margens", Icon: Percent },
    ],
  },
  {
    label: "Catálogo",
    sections: [
      { href: "/admin/produtos", label: "Produtos", Icon: Package },
      { href: "/admin/categorias", label: "Categorias", Icon: FolderTree },
      { href: "/admin/campanhas", label: "Campanhas", Icon: Tag },
    ],
  },
  {
    label: "Operação",
    sections: [
      { href: "/admin/fornecedor", label: "Fornecedor", Icon: Factory },
      { href: "/admin/atrasos", label: "Pedidos no fornecedor", Icon: Truck },
      { href: "/admin/devolucoes", label: "Devoluções", Icon: Undo2 },
    ],
  },
  {
    label: "Sistema",
    sections: [
      { href: "/admin/falhas", label: "Mensagens com falha", Icon: CircleAlert },
      { href: "/admin/manutencao", label: "Manutenção", Icon: Wrench },
    ],
  },
];

export const ADMIN_SECTIONS = ADMIN_GROUPS.flatMap((g) => g.sections);

function isCurrent(path: string, s: Section): boolean {
  return s.exact ? path === s.href : path === s.href || path.startsWith(`${s.href}/`);
}

/**
 * A navegação do painel. Em telas grandes, uma coluna fixa à esquerda com as seções em grupos: cabe uma
 * seção nova sem quebrar linha. No celular, uma barra com a seção atual e o botão "Menu", que abre a mesma
 * lista. Um só <nav>: o leitor de tela não ouve o menu duas vezes.
 */
export function AdminNav({ brand, account }: { brand: ReactNode; account: ReactNode }) {
  const path = usePathname();
  // aberto só no endereço em que foi aberto: trocar de seção já fecha o menu do celular
  const [openAt, setOpenAt] = useState<string | null>(null);
  const open = openAt === path;
  const menuId = useId();
  const current = ADMIN_SECTIONS.find((s) => isCurrent(path, s));

  return (
    <aside className="bg-hero text-on-hero flex flex-col [--focus:var(--on-hero)] lg:sticky lg:top-0 lg:h-dvh lg:overflow-y-auto">
      <div className="flex min-h-14 items-center justify-between gap-3 px-4 lg:px-5 lg:pt-5">
        {brand}
        <button
          type="button"
          aria-expanded={open}
          aria-controls={menuId}
          onClick={() => setOpenAt(open ? null : path)}
          className="hover:bg-on-hero/10 inline-flex min-h-11 cursor-pointer items-center gap-2 rounded-md px-3 font-medium lg:hidden"
        >
          {open ? (
            <X aria-hidden="true" className="size-5" />
          ) : (
            <Menu aria-hidden="true" className="size-5" />
          )}
          {open ? "Fechar" : (current?.label ?? "Menu")}
          <span className="sr-only">: menu do painel</span>
        </button>
      </div>

      <div
        id={menuId}
        // Esc fecha o menu do celular e o foco volta ao botão que o abriu
        onKeyDown={(e) => {
          // o Esc do menu de tema (um popover aqui dentro) fecha só ele
          if (e.key === "Escape" && open && !(e.target as HTMLElement).closest("[popover]")) {
            setOpenAt(null);
            document.querySelector<HTMLButtonElement>(`[aria-controls="${CSS.escape(menuId)}"]`)?.focus();
          }
        }}
        className={cn("flex flex-1 flex-col pb-4", !open && "max-lg:hidden")}
      >
        <nav aria-label="Seções do painel" className="px-2 lg:px-3 lg:pt-4">
          {ADMIN_GROUPS.map((group) => (
            <div key={group.label} className="mt-3 first:mt-0">
              <p id={`${menuId}-${group.label}`} className="text-on-hero/60 px-3 pb-1 text-sm font-medium">
                {group.label}
              </p>
              <ul aria-labelledby={`${menuId}-${group.label}`}>
                {group.sections.map((s) => {
                  const active = isCurrent(path, s);
                  return (
                    <li key={s.href}>
                      <Link
                        href={s.href}
                        aria-current={active ? "page" : undefined}
                        className={cn(
                          "text-on-hero/80 hover:text-on-hero hover:bg-on-hero/8 relative flex min-h-11 items-center gap-3 rounded-md px-3 font-medium pointer-fine:min-h-9",
                          // a seção atual: fundo e uma barra à esquerda, não só a cor
                          active &&
                            "bg-on-hero/12 text-on-hero before:bg-tag before:absolute before:inset-y-2 before:left-0 before:w-1 before:rounded-full",
                        )}
                      >
                        <s.Icon aria-hidden="true" className="size-4 shrink-0" />
                        {s.label}
                      </Link>
                    </li>
                  );
                })}
              </ul>
            </div>
          ))}
        </nav>
        <div className="border-on-hero/15 mx-4 mt-6 border-t pt-4 lg:mx-5 lg:mt-auto">{account}</div>
      </div>
    </aside>
  );
}
