import type { ProductImage as Image } from "@/lib/catalog";
import { cn } from "@/lib/utils";
import { CategoryIcon } from "./category-icon";

/**
 * A foto do produto, responsiva pelas variantes que a API manda (image.variants): o navegador escolhe
 * pelo srcset e pelo `sizes` do layout. Não passa pelo next/image: o loader dele não recebe as
 * variantes. width/height evitam salto de layout; a primeira dobra usa `priority`.
 */
export function ProductImage({
  image,
  alt,
  categorySlug,
  sizes,
  priority = false,
  className,
}: {
  image: Image | null | undefined;
  alt: string;
  categorySlug: string;
  sizes: string;
  priority?: boolean;
  className?: string;
}) {
  const frame = cn("bg-photo aspect-square w-full overflow-hidden rounded-md", className);
  if (!image) {
    return (
      <div
        role="img"
        aria-label={`${alt} (sem foto)`}
        className={cn(frame, "text-ink-soft/60 grid place-items-center")}
      >
        <CategoryIcon slug={categorySlug} className="size-1/3" />
      </div>
    );
  }
  const largest = image.variants.at(-1);
  const srcSet = image.variants.map((v) => `${v.url} ${v.width}w`).join(", ");
  return (
    <div className={frame}>
      {/* eslint-disable-next-line @next/next/no-img-element -- variantes da API; ver acima */}
      <img
        src={largest?.url ?? image.url}
        srcSet={srcSet || undefined}
        sizes={srcSet ? sizes : undefined}
        width={image.width ?? largest?.width ?? undefined}
        height={image.height ?? largest?.width ?? undefined}
        alt={alt}
        loading={priority ? "eager" : "lazy"}
        fetchPriority={priority ? "high" : "auto"}
        decoding="async"
        className="size-full object-cover"
      />
    </div>
  );
}
