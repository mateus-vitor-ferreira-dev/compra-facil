/**
 * O tema da loja: claro, escuro ou o do aparelho (o padrão). A escolha fica no localStorage e vira a classe
 * .light ou .dark no <html>, que vence a preferência do sistema no globals.css. Sem classe, vale o sistema.
 */
export type ThemeChoice = "light" | "dark" | "system";

export const THEME_KEY = "tema";

/** A cor da barra do navegador em cada tema: o papel (--paper) de cada um. */
export const THEME_COLORS = { light: "#f6f7f9", dark: "#12142a" } as const;

const CHANGE_EVENT = "tema-mudou";

export function isThemeChoice(value: unknown): value is ThemeChoice {
  return value === "light" || value === "dark" || value === "system";
}

export function readThemeChoice(): ThemeChoice {
  try {
    const saved = window.localStorage.getItem(THEME_KEY);
    return isThemeChoice(saved) ? saved : "system";
  } catch {
    return "system"; // navegação privada que bloqueia o storage: segue o aparelho
  }
}

function systemPrefersDark(): boolean {
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

/** Claro ou escuro de fato, já com o "como o aparelho" resolvido. */
export function resolvedTheme(choice: ThemeChoice): "light" | "dark" {
  if (choice === "system") return systemPrefersDark() ? "dark" : "light";
  return choice;
}

/**
 * Aplica a escolha no <html> e na barra do navegador. Com uma escolha fixa, as duas metas de theme-color (a
 * do claro e a do escuro, por media query) recebem a cor dela; no "como o aparelho", cada uma volta à sua.
 */
export function applyThemeChoice(choice: ThemeChoice): void {
  const html = document.documentElement;
  html.classList.toggle("dark", choice === "dark");
  html.classList.toggle("light", choice === "light");
  for (const meta of document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')) {
    const own = meta.media.includes("dark") ? THEME_COLORS.dark : THEME_COLORS.light;
    meta.content = choice === "system" ? own : THEME_COLORS[choice];
  }
}

/** Guarda e aplica, sem as transições de cor dos botões: o tema troca de uma vez, não em ondas. */
export function setThemeChoice(choice: ThemeChoice): void {
  const pause = document.createElement("style");
  pause.textContent = "*,*::before,*::after{transition:none!important}";
  document.head.appendChild(pause);
  try {
    if (choice === "system") window.localStorage.removeItem(THEME_KEY);
    else window.localStorage.setItem(THEME_KEY, choice);
  } catch {
    // sem storage, a escolha vale só nesta página
  }
  applyThemeChoice(choice);
  window.dispatchEvent(new Event(CHANGE_EVENT));
  // força o estilo novo antes de devolver as transições
  void getComputedStyle(document.body).color;
  requestAnimationFrame(() => pause.remove());
}

/** Para o useSyncExternalStore: muda nesta aba, em outra aba (storage) e no aparelho (media query). */
export function subscribeTheme(onChange: () => void): () => void {
  const media = window.matchMedia("(prefers-color-scheme: dark)");
  const onStorage = (e: StorageEvent) => {
    if (e.key !== THEME_KEY && e.key !== null) return;
    applyThemeChoice(readThemeChoice());
    onChange();
  };
  window.addEventListener(CHANGE_EVENT, onChange);
  window.addEventListener("storage", onStorage);
  media.addEventListener("change", onChange);
  return () => {
    window.removeEventListener(CHANGE_EVENT, onChange);
    window.removeEventListener("storage", onStorage);
    media.removeEventListener("change", onChange);
  };
}

/**
 * O script do <head>: roda antes do primeiro desenho e põe a classe da escolha salva, para a página não
 * nascer clara e virar escura. Sem dependências: é serializado como texto.
 */
export const themeScript = `(function(){try{var t=localStorage.getItem(${JSON.stringify(THEME_KEY)});if(t!=="light"&&t!=="dark")return;var h=document.documentElement;h.classList.add(t);var c=t==="dark"?${JSON.stringify(THEME_COLORS.dark)}:${JSON.stringify(THEME_COLORS.light)};document.querySelectorAll('meta[name="theme-color"]').forEach(function(m){m.content=c})}catch(e){}})()`;
