import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  applyThemeChoice,
  readThemeChoice,
  resolvedTheme,
  setThemeChoice,
  THEME_COLORS,
  THEME_KEY,
  themeScript,
} from "./theme";

function mockSystem(dark: boolean) {
  vi.stubGlobal(
    "matchMedia",
    (query: string) =>
      ({
        matches: dark && query.includes("dark"),
        media: query,
        addEventListener: () => {},
        removeEventListener: () => {},
      }) as unknown as MediaQueryList,
  );
}

function addThemeMetas() {
  for (const scheme of ["light", "dark"]) {
    const meta = document.createElement("meta");
    meta.name = "theme-color";
    meta.media = `(prefers-color-scheme: ${scheme})`;
    meta.content = scheme === "dark" ? THEME_COLORS.dark : THEME_COLORS.light;
    document.head.appendChild(meta);
  }
}

const metas = () =>
  [...document.querySelectorAll<HTMLMetaElement>('meta[name="theme-color"]')].map((m) => m.content);

describe("tema", () => {
  beforeEach(() => {
    localStorage.clear();
    document.documentElement.className = "";
    document.head.innerHTML = "";
    addThemeMetas();
    mockSystem(false);
  });
  afterEach(() => vi.unstubAllGlobals());

  it("sem escolha salva (ou com lixo), segue o aparelho", () => {
    expect(readThemeChoice()).toBe("system");
    localStorage.setItem(THEME_KEY, "roxo");
    expect(readThemeChoice()).toBe("system");
    mockSystem(true);
    expect(resolvedTheme("system")).toBe("dark");
    expect(resolvedTheme("light")).toBe("light");
  });

  it("escolher escuro salva, põe a classe e pinta a barra do navegador nos dois media", () => {
    setThemeChoice("dark");
    expect(localStorage.getItem(THEME_KEY)).toBe("dark");
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(document.documentElement.classList.contains("light")).toBe(false);
    expect(metas()).toEqual([THEME_COLORS.dark, THEME_COLORS.dark]);
  });

  it("voltar para o aparelho apaga a escolha e devolve cada meta à sua cor", () => {
    setThemeChoice("light");
    setThemeChoice("system");
    expect(localStorage.getItem(THEME_KEY)).toBeNull();
    expect(document.documentElement.className).toBe("");
    expect(metas()).toEqual([THEME_COLORS.light, THEME_COLORS.dark]);
  });

  it("a troca não deixa para trás o estilo que pausa as transições", async () => {
    setThemeChoice("dark");
    await new Promise((r) => requestAnimationFrame(r));
    expect(document.head.querySelectorAll("style")).toHaveLength(0);
  });

  it("o script do <head> aplica a escolha salva e ignora o resto", () => {
    localStorage.setItem(THEME_KEY, "dark");
    new Function(themeScript)();
    expect(document.documentElement.classList.contains("dark")).toBe(true);
    expect(metas()).toEqual([THEME_COLORS.dark, THEME_COLORS.dark]);

    document.documentElement.className = "";
    localStorage.setItem(THEME_KEY, "<script>");
    new Function(themeScript)();
    expect(document.documentElement.className).toBe("");
  });

  it("aplicar sem storage disponível não quebra", () => {
    vi.spyOn(Storage.prototype, "getItem").mockImplementation(() => {
      throw new Error("bloqueado");
    });
    expect(readThemeChoice()).toBe("system");
    expect(() => applyThemeChoice("light")).not.toThrow();
    vi.restoreAllMocks();
  });
});
