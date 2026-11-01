import Link from "next/link";
import type { Product } from "@/lib/catalog";
import { Badge } from "@/components/ui/badge";
import { Skeleton } from "@/components/ui/skeleton";
import { PaymentTerms } from "./payment-terms";
import { PriceTag } from "./price-tag";
import { ProductImage } from "./product-image";

/** larguras da grade (2 colunas no celular, 3 no tablet, 4 no desktop): o srcset escolhe a foto por elas */
export const PRODUCT_GRID_SIZES = "(min-width: 1024px) 22vw, (min-width: 640px) 30vw, 46vw";

/**
 * O produto na grade. O link cobre o card inteiro (um só ponto de foco por produto); sem sombra nem
 * borda: a foto em fundo próprio, o nome e a etiqueta dão a hierarquia.
 */
export function ProductCard({ product, priority = false }: { product: Product; priority?: boolean }) {
  return (
    // o foco do teclado contorna o card inteiro, não só o nome (o link cobre o card)
    <article className="group has-[a:focus-visible]:outline-focus relative flex h-full flex-col gap-3 rounded-md has-[a:focus-visible]:outline-2 has-[a:focus-visible]:outline-offset-4">
      <ProductImage
        image={product.image}
        alt={product.name}
        categorySlug={product.category.slug}
        sizes={PRODUCT_GRID_SIZES}
        priority={priority}
        className="transition-opacity group-hover:opacity-90"
      />
      {product.campaign && (
        // em tinta: o amarelo fica para o preço, que numa grade de ofertas já aparece em todo card
        <Badge variant="campaign" className="absolute top-2 left-2">
          {product.campaign.name}
        </Badge>
      )}
      <h3 className="line-clamp-2 text-base leading-snug font-medium sm:text-sm">
        <Link
          href={`/produto/${product.id}`}
          title={product.name}
          className="outline-none after:absolute after:inset-0 hover:underline"
        >
          {product.name}
        </Link>
      </h3>
      <div className="mt-auto space-y-1">
        <PriceTag price={product.price} effective={product.effective_price} />
        <PaymentTerms price={product.effective_price} />
      </div>
    </article>
  );
}

export function ProductCardSkeleton() {
  return (
    <div className="flex flex-col gap-3" aria-hidden="true">
      <Skeleton className="aspect-square w-full" />
      <Skeleton className="h-4 w-4/5" />
      <Skeleton className="h-4 w-3/5" />
      <Skeleton className="h-8 w-28" />
    </div>
  );
}
