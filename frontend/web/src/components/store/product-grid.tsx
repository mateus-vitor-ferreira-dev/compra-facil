import type { ReactNode } from "react";
import { ProductCard, ProductCardSkeleton } from "@/components/commerce/product-card";
import type { Product } from "@/lib/catalog";
import { cn } from "@/lib/utils";

const grid = "grid grid-cols-2 gap-x-4 gap-y-8 sm:grid-cols-3 lg:grid-cols-4";

/** A grade de produtos das listagens: 2 colunas no celular, 3 no tablet, 4 no desktop. */
export function ProductGrid({
  products,
  label,
  priorityCount = 0,
  children,
  className,
}: {
  products: Product[];
  label: string;
  /** quantos cards da primeira dobra carregam a foto com prioridade */
  priorityCount?: number;
  /** cards a mais no fim da lista (o "ver mais" acrescenta aqui) */
  children?: ReactNode;
  className?: string;
}) {
  return (
    <ul aria-label={label} className={cn(grid, className)}>
      {products.map((p, i) => (
        <li key={p.id}>
          <ProductCard product={p} priority={i < priorityCount} />
        </li>
      ))}
      {children}
    </ul>
  );
}

export function ProductGridSkeleton({ count = 8 }: { count?: number }) {
  return (
    <div className={grid}>
      {Array.from({ length: count }, (_, i) => (
        <ProductCardSkeleton key={i} />
      ))}
    </div>
  );
}
