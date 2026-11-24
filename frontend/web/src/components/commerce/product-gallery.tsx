"use client";

import { useEffect, useRef, useState, type KeyboardEvent } from "react";
import type { ProductImage as Image } from "@/lib/catalog";
import { announceColor, colorsByPhoto, onColor } from "@/lib/product-color-sync";
import { cn } from "@/lib/utils";
import { ProductImage } from "./product-image";

/**
 * As fotos do produto (#207): uma faixa com scroll-snap, que o dedo arrasta sem JavaScript, e as miniaturas
 * para ir direto a uma foto. As setas e Home/End andam pelas fotos, com o foco na faixa ou nas miniaturas.
 * Cada foto tem o próprio srcset, e só a primeira é de primeira dobra.
 *
 * Com `productId` e `colors`, a foto acompanha a cor: escolher a cor no formulário traz a foto dela, e a miniatura
 * de uma cor marca essa cor no formulário (ver lib/product-color-sync).
 */
export function ProductGallery({
  images,
  alt,
  categorySlug,
  sizes,
  productId,
  colors,
}: {
  images: Image[];
  alt: string;
  categorySlug: string;
  sizes: string;
  productId?: string;
  colors?: string[];
}) {
  const [current, setCurrent] = useState(0);
  const rail = useRef<HTMLDivElement>(null);
  const thumbs = useRef<(HTMLButtonElement | null)[]>([]);
  const byPhoto = colorsByPhoto(colors, images.length);

  // a cor escolhida no formulário traz a foto dela
  useEffect(() => {
    if (!productId || !byPhoto) return;
    return onColor(productId, "seletor", (color) => {
      const index = byPhoto.indexOf(color);
      if (index < 0) return;
      rail.current?.scrollTo({ left: index * rail.current.clientWidth });
      setCurrent(index);
    });
  }, [productId, byPhoto]);

  if (images.length <= 1) {
    return <ProductImage image={images[0]} alt={alt} categorySlug={categorySlug} sizes={sizes} priority />;
  }

  // o motion-safe:scroll-smooth da faixa anima, e quem pede menos movimento vai direto
  const show = (index: number) => {
    const el = rail.current;
    if (el) el.scrollTo({ left: index * el.clientWidth });
    setCurrent(index);
  };

  const onKeyDown = (event: KeyboardEvent) => {
    const last = images.length - 1;
    const target = {
      ArrowRight: Math.min(current + 1, last),
      ArrowLeft: Math.max(current - 1, 0),
      Home: 0,
      End: last,
    }[event.key];
    if (target === undefined) return;
    event.preventDefault(); // a faixa rolaria sozinha também
    show(target);
    if (event.target instanceof HTMLButtonElement) thumbs.current[target]?.focus();
  };

  return (
    <div role="group" aria-label={`Fotos de ${alt}`} className="min-w-0 space-y-3" onKeyDown={onKeyDown}>
      <div
        ref={rail}
        tabIndex={0}
        aria-label="Foto em destaque; use as setas para trocar"
        role="group"
        onScroll={(e) => {
          const { scrollLeft, clientWidth } = e.currentTarget;
          if (clientWidth > 0) setCurrent(Math.round(scrollLeft / clientWidth));
        }}
        className="flex snap-x snap-mandatory [scrollbar-width:none] overflow-x-auto rounded-md focus-visible:outline-2 motion-safe:scroll-smooth [&::-webkit-scrollbar]:hidden"
      >
        {images.map((image, i) => (
          <div key={image.url} className="w-full shrink-0 snap-center snap-always">
            <ProductImage
              image={image}
              alt={i === 0 ? alt : `${alt}, foto ${i + 1} de ${images.length}`}
              categorySlug={categorySlug}
              sizes={sizes}
              priority={i === 0}
            />
          </div>
        ))}
      </div>

      {/* folga de 6px: o overflow corta o anel de foco (2px + 2px de afastamento) das miniaturas (#273) */}
      <ul className="-m-1 flex [scrollbar-width:none] gap-2 overflow-x-auto p-1.5">
        {images.map((image, i) => (
          <li key={image.url} className="shrink-0">
            <button
              ref={(el) => {
                thumbs.current[i] = el;
              }}
              type="button"
              aria-label={
                byPhoto?.[i]
                  ? `Ver a foto ${i + 1} de ${images.length}, cor ${byPhoto[i]}`
                  : `Ver a foto ${i + 1} de ${images.length}`
              }
              aria-current={i === current ? "true" : undefined}
              onClick={() => {
                show(i);
                // a miniatura de uma cor marca a cor no formulário
                if (productId && byPhoto?.[i]) announceColor(productId, byPhoto[i], "galeria");
              }}
              className={cn(
                "block size-16 cursor-pointer overflow-hidden rounded-md border-2 p-0.5 sm:size-20",
                i === current ? "border-ink" : "hover:border-ink/40 border-transparent",
              )}
            >
              <ProductImage
                image={image}
                alt=""
                categorySlug={categorySlug}
                sizes="80px"
                className="rounded-sm"
              />
            </button>
          </li>
        ))}
      </ul>

      <p className="sr-only" aria-live="polite">
        Foto {current + 1} de {images.length}
      </p>
    </div>
  );
}
