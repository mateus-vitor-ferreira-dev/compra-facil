import {
  BedDouble,
  Bike,
  CookingPot,
  Cpu,
  Dumbbell,
  Headphones,
  House,
  Keyboard,
  Laptop,
  Monitor,
  MonitorSmartphone,
  Package,
  Refrigerator,
  Sofa,
  Speaker,
  Trophy,
  Tv,
  WashingMachine,
  type LucideIcon,
} from "lucide-react";

/** Um ícone por categoria do catálogo (slug), para a navegação e o placeholder sem foto. */
const icons: Record<string, LucideIcon> = {
  eletronicos: MonitorSmartphone,
  tv: Tv,
  fones: Headphones,
  "caixas-de-som": Speaker,
  informatica: Cpu,
  notebooks: Laptop,
  monitores: Monitor,
  perifericos: Keyboard,
  eletrodomesticos: Refrigerator,
  geladeiras: Refrigerator,
  fogoes: CookingPot,
  lavadoras: WashingMachine,
  casa: House,
  sofas: Sofa,
  colchoes: BedDouble,
  esporte: Trophy,
  bicicletas: Bike,
  fitness: Dumbbell,
};

export function CategoryIcon({ slug, className }: { slug: string; className?: string }) {
  const Icon = icons[slug] ?? Package;
  return <Icon aria-hidden="true" strokeWidth={1.5} className={className} />;
}
