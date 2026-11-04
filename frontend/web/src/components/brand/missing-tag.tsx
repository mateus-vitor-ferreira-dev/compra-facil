import { cn } from "@/lib/utils";

/**
 * A etiqueta da página que não existe: a mesma das campanhas (o amarelo, o canto recortado e o furo), pendurada
 * por um barbante, com o 404 no lugar do preço. Balança uma vez ao abrir; com "reduzir movimento", fica parada.
 */
export function MissingTag({ size = "lg", className }: { size?: "lg" | "sm"; className?: string }) {
  return (
    <div
      aria-hidden="true"
      // o conjunto balança a partir do prego (o topo), barbante e etiqueta juntos
      className={cn("animate-tag-swing relative inline-flex origin-top flex-col items-center", className)}
    >
      {/* o barbante, do prego até o furo */}
      <svg viewBox="0 0 40 64" className={cn("text-ink/55", size === "lg" ? "h-16 w-10" : "h-10 w-6")}>
        <circle cx="20" cy="5" r="4" className="fill-current" />
        <path
          d="M20 8 C 14 28, 26 40, 20 62"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
        />
      </svg>
      <div
        className={cn(
          "bg-tag text-tag-ink font-condensed relative -mt-1 rounded-sm font-extrabold",
          // o recorte do canto, apontando para cima, onde o barbante prende
          "[clip-path:polygon(50%_0,100%_14%,100%_100%,0_100%,0_14%)]",
          size === "lg"
            ? "px-10 pt-14 pb-8 text-[7rem] leading-none sm:text-[9rem]"
            : "px-6 pt-9 pb-5 text-6xl leading-none",
        )}
      >
        <span
          className={cn(
            "bg-paper absolute left-1/2 -translate-x-1/2 rounded-full",
            size === "lg" ? "top-5 size-4" : "top-3 size-3",
          )}
        />
        404
      </div>
    </div>
  );
}
