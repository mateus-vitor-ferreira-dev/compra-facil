"use client";

import { Moon, Sun } from "lucide-react";
import { useSyncExternalStore } from "react";
import { Button } from "@/components/ui/button";

/** Alterna claro/escuro na página do design system (a loja segue o tema do sistema). */
function isDark(): boolean {
  const html = document.documentElement;
  if (html.classList.contains("dark")) return true;
  if (html.classList.contains("light")) return false;
  return window.matchMedia("(prefers-color-scheme: dark)").matches;
}

function subscribe(onChange: () => void): () => void {
  const media = window.matchMedia("(prefers-color-scheme: dark)");
  const observer = new MutationObserver(onChange);
  observer.observe(document.documentElement, { attributes: true, attributeFilter: ["class"] });
  media.addEventListener("change", onChange);
  return () => {
    observer.disconnect();
    media.removeEventListener("change", onChange);
  };
}

export function ThemeToggle() {
  // o tema de verdade (classe no <html> ou o do sistema), não um estado que começa em "claro"
  const dark = useSyncExternalStore(subscribe, isDark, () => false);
  return (
    <Button
      variant="secondary"
      size="sm"
      aria-pressed={dark}
      onClick={() => {
        const next = !dark;
        document.documentElement.classList.toggle("dark", next);
        document.documentElement.classList.toggle("light", !next);
      }}
    >
      {dark ? <Sun aria-hidden /> : <Moon aria-hidden />}
      {dark ? "Tema claro" : "Tema escuro"}
    </Button>
  );
}
