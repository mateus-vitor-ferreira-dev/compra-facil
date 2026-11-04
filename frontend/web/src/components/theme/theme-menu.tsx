"use client";

import { Check, Monitor, Moon, Sun } from "lucide-react";
import { useEffect, useId, useRef, useSyncExternalStore } from "react";
import {
  applyThemeChoice,
  readThemeChoice,
  setThemeChoice,
  subscribeTheme,
  THEME_COLORS,
  type ThemeChoice,
} from "@/lib/theme";
import { cn } from "@/lib/utils";

const OPTIONS: { value: ThemeChoice; label: string; Icon: typeof Sun }[] = [
  { value: "light", label: "Claro", Icon: Sun },
  { value: "dark", label: "Escuro", Icon: Moon },
  { value: "system", label: "Como o aparelho", Icon: Monitor },
];

function useThemeChoice(): ThemeChoice {
  return useSyncExternalStore(subscribeTheme, readThemeChoice, () => "system" as const);
}

/**
 * A amostra de cada opção: uma etiqueta de preço no papel e na tinta daquele tema (no "como o aparelho",
 * metade de cada). Cores fixas de propósito: mostram o outro tema, não o atual.
 */
function Swatch({ theme }: { theme: ThemeChoice }) {
  const light = { paper: THEME_COLORS.light, ink: "#1b1f3b" };
  const dark = { paper: THEME_COLORS.dark, ink: "#eef0f7" };
  const clip = `amostra-${useId().replace(/:/g, "")}`;
  const ink = (theme === "light" ? light : dark).ink;
  return (
    <svg
      viewBox="0 0 36 24"
      aria-hidden="true"
      className="h-6 w-9 shrink-0 rounded-sm ring-1 ring-current/25"
    >
      <defs>
        <clipPath id={clip}>
          <polygon points="0,0 36,0 0,24" />
        </clipPath>
      </defs>
      {theme === "system" ? (
        <>
          <rect width="36" height="24" fill={dark.paper} />
          <rect width="36" height="24" fill={light.paper} clipPath={`url(#${clip})`} />
        </>
      ) : (
        <rect width="36" height="24" fill={(theme === "dark" ? dark : light).paper} />
      )}
      {/* a etiqueta amarela de campanha e duas linhas de texto, como no card de produto */}
      <path d="M22 5h9v6l-2 2h-7z" fill="#ffd23f" />
      <rect x="5" y="14" width="14" height="2.5" rx="1" fill={ink} />
      <rect x="5" y="18.5" width="9" height="2.5" rx="1" fill={ink} opacity="0.55" />
    </svg>
  );
}

/**
 * As três opções como rádios: as setas trocam e já mostram o tema, e o leitor de tela anuncia "1 de 3,
 * selecionado". `onPick` roda no clique de verdade (mouse, toque) e no Enter, não na seta: é o "pronto".
 */
function ThemeOptions({ layout, onPick }: { layout: "stack" | "row"; onPick?: () => void }) {
  const choice = useThemeChoice();
  const name = useId();
  return (
    <fieldset>
      <legend
        className={cn("font-medium", layout === "stack" ? "text-ink-soft px-2 pt-1 pb-2 text-sm" : "mb-2")}
      >
        Tema da loja
      </legend>
      <div className={cn(layout === "row" && "flex flex-wrap gap-2")}>
        {OPTIONS.map(({ value, label }) => (
          <label
            key={value}
            onClick={(e) => {
              // o clique que a seta gera no rádio tem detail 0: esse não fecha
              if (e.detail > 0) onPick?.();
            }}
            className={cn(
              "has-[:focus-visible]:outline-focus flex min-h-11 cursor-pointer items-center gap-3 rounded-md px-2 has-[:focus-visible]:outline-2 has-[:focus-visible]:outline-offset-2",
              layout === "stack"
                ? "hover:bg-ink/6 has-[:checked]:bg-ink/8"
                : "border-line hover:border-ink/60 has-[:checked]:border-ink border pr-3 pointer-fine:min-h-9",
            )}
          >
            <input
              type="radio"
              name={name}
              value={value}
              checked={choice === value}
              onChange={() => setThemeChoice(value)}
              onKeyDown={(e) => {
                if (e.key === "Enter") onPick?.();
              }}
              className="sr-only"
            />
            <Swatch theme={value} />
            <span className={cn(layout === "stack" && "flex-1")}>{label}</span>
            {/* o visto marca a opção em uso também sem depender da cor (a borda, no rodapé) */}
            {choice === value && <Check aria-hidden="true" className="size-4" />}
          </label>
        ))}
      </div>
    </fieldset>
  );
}

/**
 * O tema no cabeçalho: um botão com o tema escolhido que abre as opções num popover nativo (Esc e clique
 * fora fecham sem JavaScript nosso; o foco volta ao botão).
 */
export function ThemeMenu({ className }: { className?: string }) {
  const choice = useThemeChoice();
  const popoverId = useId();
  const popover = useRef<HTMLDivElement>(null);
  const current = OPTIONS.find((o) => o.value === choice) ?? OPTIONS[2]!;

  // as metas de theme-color podem chegar depois do script do <head>: acerta a cor da barra ao montar
  useEffect(() => applyThemeChoice(readThemeChoice()), []);

  return (
    <>
      <button
        type="button"
        popoverTarget={popoverId}
        className={cn(
          "theme-anchor text-ink hover:bg-ink/8 inline-flex size-11 cursor-pointer items-center justify-center rounded-md",
          className,
        )}
      >
        <current.Icon aria-hidden="true" className="size-5" />
        <span className="sr-only">Tema: {current.label}</span>
      </button>
      <div
        ref={popover}
        id={popoverId}
        popover="auto"
        className="theme-popover bg-surface text-ink border-line w-64 rounded-lg border p-2 shadow-lg"
      >
        <ThemeOptions layout="stack" onPick={() => popover.current?.hidePopover()} />
      </div>
    </>
  );
}

/** O tema no rodapé, à mostra: o caminho no celular, onde o cabeçalho não tem lugar para o botão. */
export function ThemeChooser() {
  return <ThemeOptions layout="row" />;
}
