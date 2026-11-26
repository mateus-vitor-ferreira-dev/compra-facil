/** As seções do painel, fora do componente do menu (que é de cliente): a página do manual também as lê. */
import {
  BookOpen,
  ChartColumn,
  ClipboardList,
  CircleAlert,
  Factory,
  FolderTree,
  Package,
  Percent,
  ReceiptText,
  ShieldCheck,
  Tag,
  Truck,
  Undo2,
  Wrench,
  type LucideIcon,
} from "lucide-react";

export type Section = { href: string; label: string; Icon: LucideIcon; exact?: boolean };

/** As seções do painel, pelo que a equipe vai fazer: vender, cuidar do catálogo, operar e manter. */
export const ADMIN_GROUPS: { label: string; sections: Section[] }[] = [
  {
    label: "Vendas",
    sections: [
      { href: "/admin", label: "Visão geral", Icon: ChartColumn, exact: true },
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
      { href: "/admin/pedidos-a-fazer", label: "Pedidos a fazer", Icon: ClipboardList },
      { href: "/admin/atrasos", label: "Pedidos no fornecedor", Icon: Truck },
      { href: "/admin/devolucoes", label: "Devoluções", Icon: Undo2 },
    ],
  },
  {
    label: "Sistema",
    sections: [
      { href: "/admin/falhas", label: "Mensagens com falha", Icon: CircleAlert },
      { href: "/admin/manutencao", label: "Manutenção", Icon: Wrench },
      { href: "/admin/seguranca", label: "Segurança da conta", Icon: ShieldCheck },
      { href: "/admin/manual", label: "Manual", Icon: BookOpen },
    ],
  },
];

export const ADMIN_SECTIONS = ADMIN_GROUPS.flatMap((g) => g.sections);
