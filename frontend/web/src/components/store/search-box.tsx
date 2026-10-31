"use client";

import { Search } from "lucide-react";
import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { Suspense, useEffect, useId, useRef, useState } from "react";
import { cn } from "@/lib/utils";

export type Suggestion =
  | { kind: "category"; slug: string; name: string }
  | { kind: "product"; id: string; name: string; category: string };

const MIN_CHARS = 2; // a busca do catálogo exige 2 caracteres

/**
 * A busca do cabeçalho: um formulário GET para /busca (funciona sem JavaScript) com sugestões no
 * padrão combobox da WAI-ARIA. Setas andam pela lista, Enter abre a sugestão marcada (ou busca o
 * texto), Esc fecha. As sugestões vêm de /api/busca/sugestoes, 200 ms depois da última tecla.
 */
export function SearchBox({ defaultValue = "", className }: { defaultValue?: string; className?: string }) {
  const router = useRouter();
  const id = useId();
  const listId = `${id}-sugestoes`;
  const [value, setValue] = useState(defaultValue);
  const [items, setItems] = useState<Suggestion[]>([]);
  const [open, setOpen] = useState(false);
  const [active, setActive] = useState(-1);
  const [ready, setReady] = useState(false); // hidratado: as sugestões já respondem ao teclado
  const box = useRef<HTMLDivElement>(null);
  const input = useRef<HTMLInputElement>(null);

  // o que foi digitado antes da hidratação não passou pelo onChange: o campo é a fonte da verdade
  useEffect(() => {
    const typed = input.current?.value ?? "";
    if (typed !== defaultValue) {
      setValue(typed);
      if (document.activeElement === input.current) setOpen(true);
    }
    setReady(true);
  }, [defaultValue]);

  const term = value.trim();

  useEffect(() => {
    if (term.length < MIN_CHARS) return;
    const controller = new AbortController();
    const wait = setTimeout(async () => {
      try {
        const res = await fetch(`/api/busca/sugestoes?${new URLSearchParams({ q: term })}`, {
          signal: controller.signal,
        });
        if (!res.ok) return setItems([]);
        setItems(((await res.json()) as { data: Suggestion[] }).data);
        setActive(-1);
      } catch {
        // digitou de novo (abortou) ou a rede caiu: a busca pelo formulário continua funcionando
      }
    }, 200);
    return () => {
      clearTimeout(wait);
      controller.abort();
    };
  }, [term]);

  // sem sugestão marcada, o Enter envia o formulário e busca o texto
  const options = term.length >= MIN_CHARS ? items : [];
  const expanded = open && options.length > 0;

  function href(s: Suggestion) {
    return s.kind === "category" ? `/categoria/${s.slug}` : `/produto/${s.id}`;
  }

  function go(path: string) {
    setOpen(false);
    router.push(path);
  }

  function onKeyDown(event: React.KeyboardEvent<HTMLInputElement>) {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      if (!options.length) return;
      event.preventDefault();
      setOpen(true);
      const step = event.key === "ArrowDown" ? 1 : -1;
      // anda em círculo pelas sugestões passando pelo campo (-1): o texto digitado volta a valer
      setActive((i) => ((i + 1 + step + options.length + 1) % (options.length + 1)) - 1);
    } else if (event.key === "Escape") {
      if (expanded) {
        event.preventDefault();
        setOpen(false);
        setActive(-1);
      } else {
        setValue("");
      }
    } else if (event.key === "Enter" && expanded) {
      const chosen = options[active];
      if (!chosen) return; // nada marcado: o formulário busca o texto
      event.preventDefault();
      go(href(chosen));
    }
  }

  return (
    <div
      ref={box}
      className={cn("relative", className)}
      onBlur={(e) => {
        if (!box.current?.contains(e.relatedTarget as Node)) setOpen(false);
      }}
    >
      <form role="search" action="/busca" data-ready={ready || undefined} className="relative">
        <label htmlFor={`${id}-q`} className="sr-only">
          Buscar produtos
        </label>
        <input
          ref={input}
          id={`${id}-q`}
          name="q"
          type="search"
          role="combobox"
          aria-autocomplete="list"
          aria-expanded={expanded}
          aria-controls={listId}
          aria-activedescendant={
            expanded && active >= 0 && active < options.length ? `${listId}-${active}` : undefined
          }
          autoComplete="off"
          enterKeyHint="search"
          minLength={MIN_CHARS}
          required
          placeholder="Buscar produtos, marcas e categorias"
          value={value}
          onChange={(e) => {
            setValue(e.target.value);
            setOpen(true);
          }}
          onFocus={() => setOpen(true)}
          onKeyDown={onKeyDown}
          className="bg-surface border-field-line text-ink placeholder:text-ink-soft focus-visible:border-ink h-11 w-full rounded-md border pr-12 pl-4 text-base outline-none focus-visible:outline-2 focus-visible:outline-offset-0"
        />
        <button
          type="submit"
          className="text-ink hover:bg-ink/8 absolute top-0 right-0 inline-flex size-11 cursor-pointer items-center justify-center rounded-md"
        >
          <Search aria-hidden="true" className="size-5" />
          <span className="sr-only">Buscar</span>
        </button>
      </form>

      <ul
        id={listId}
        role="listbox"
        aria-label="Sugestões"
        hidden={!expanded}
        className="bg-surface border-line absolute inset-x-0 top-full z-40 mt-1 overflow-hidden rounded-md border py-1 shadow-lg"
      >
        {options.map((s, i) => (
          <li
            key={s.kind === "category" ? `c-${s.slug}` : s.id}
            id={`${listId}-${i}`}
            role="option"
            aria-selected={i === active}
            // mousedown, não click: o clique chega antes do blur fechar a lista
            onMouseDown={(e) => {
              e.preventDefault();
              go(href(s));
            }}
            onMouseEnter={() => setActive(i)}
            className={cn(
              "flex min-h-11 cursor-pointer flex-col justify-center px-4 py-2",
              i === active && "bg-ink/8",
            )}
          >
            {s.kind === "category" ? (
              <span>
                <span className="text-ink-soft">Categoria: </span>
                <span className="font-semibold">{s.name}</span>
              </span>
            ) : (
              <>
                <span className="line-clamp-1">{s.name}</span>
                <span className="text-ink-soft text-sm">{s.category}</span>
              </>
            )}
          </li>
        ))}
      </ul>
    </div>
  );
}

/** Na página de resultados, o campo já vem com o termo buscado (e troca quando a busca muda). */
function SearchFromUrl({ className }: { className?: string }) {
  const params = useSearchParams();
  const q = usePathname() === "/busca" ? (params.get("q") ?? "") : "";
  return <SearchBox key={q} defaultValue={q} className={className} />;
}

export function HeaderSearch({ className }: { className?: string }) {
  return (
    <Suspense fallback={<SearchBox className={className} />}>
      <SearchFromUrl className={className} />
    </Suspense>
  );
}
