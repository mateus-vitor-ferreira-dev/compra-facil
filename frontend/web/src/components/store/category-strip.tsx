"use client";

import { ChevronLeft, ChevronRight } from "lucide-react";
import { useEffect, useRef, useState, type ReactNode } from "react";
import { cn } from "@/lib/utils";

/**
 * A linha de categorias do desktop (#317). Quando não cabe (de 768 a 1279 px, ou com um departamento novo), a
 * lista rola para o lado sem barra, e só o item cortado indicava que havia mais. Agora a borda do lado que tem
 * mais esmaece e ganha uma seta, que rola um pedaço: quem usa mouse sem trackpad alcança tudo. As setas ficam
 * fora do Tab e do leitor de tela, porque os links já são alcançáveis pelo teclado, e o foco traz o item à vista.
 * Sem JavaScript, a lista rola como antes.
 */
export function CategoryStrip({ children }: { children: ReactNode }) {
  const list = useRef<HTMLUListElement>(null);
  const [more, setMore] = useState({ before: false, after: false });

  useEffect(() => {
    const el = list.current;
    if (!el) return;
    const update = () =>
      setMore({
        before: el.scrollLeft > 1,
        after: el.scrollLeft + el.clientWidth < el.scrollWidth - 1,
      });
    update();
    el.addEventListener("scroll", update, { passive: true });
    const resize = new ResizeObserver(update);
    resize.observe(el);
    return () => {
      el.removeEventListener("scroll", update);
      resize.disconnect();
    };
  }, []);

  const scroll = (direction: 1 | -1) => {
    const el = list.current;
    if (!el) return;
    const reduce = window.matchMedia("(prefers-reduced-motion: reduce)").matches;
    el.scrollBy({ left: direction * el.clientWidth * 0.7, behavior: reduce ? "auto" : "smooth" });
  };

  const arrow =
    "bg-paper text-ink hover:bg-line absolute top-0 z-10 hidden h-10 w-10 cursor-pointer items-center justify-center rounded-md md:flex [&_svg]:size-4";
  // o esmaecido fica ao lado da seta, que tem o fundo da página: o item cortado some aos poucos
  const fade = "from-paper pointer-events-none absolute top-0 h-10 w-12";

  return (
    <div className="relative hidden md:block">
      <ul
        ref={list}
        className="flex scroll-px-10 [scrollbar-width:none] gap-x-3 overflow-x-auto xl:justify-between [&::-webkit-scrollbar]:hidden"
      >
        {children}
      </ul>
      {more.before && (
        <>
          <div aria-hidden="true" className={cn(fade, "left-10 bg-linear-to-r")} />
          <button
            type="button"
            tabIndex={-1}
            aria-hidden="true"
            onClick={() => scroll(-1)}
            className={cn(arrow, "left-0")}
          >
            <ChevronLeft />
          </button>
        </>
      )}
      {more.after && (
        <>
          <div aria-hidden="true" className={cn(fade, "right-10 bg-linear-to-l")} />
          <button
            type="button"
            tabIndex={-1}
            aria-hidden="true"
            onClick={() => scroll(1)}
            className={cn(arrow, "right-0")}
          >
            <ChevronRight />
          </button>
        </>
      )}
    </div>
  );
}
