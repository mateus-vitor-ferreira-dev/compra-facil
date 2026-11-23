import { existsSync } from "node:fs";
import { join } from "node:path";
import { describe, expect, it } from "vitest";
import data from "./ilustradas.json";
import {
  ILLUSTRATED_OPTIONS,
  catalogTemplateId,
  illustrationUrl,
  linesWithText,
  resolveIllustrated,
  variantFor,
  type IllustratedTemplate,
} from "./illustrated";

const all = data as IllustratedTemplate[];
// o formato de id de arte que o catalog aceita (personalization.template)
const CATALOG_ID = /^[a-z0-9]+(-[a-z0-9]+)*(:[a-z0-9]+(-[a-z0-9]+)*)?$/;

describe("artes ilustradas", () => {
  it("todo id vira um id que o catalog aceita, e resolve de volta", () => {
    for (const t of all) {
      const id = catalogTemplateId(t.id);
      expect(id, t.id).toMatch(CATALOG_ID);
      expect(id.length, t.id).toBeLessThanOrEqual(40);
      expect(resolveIllustrated(id)).toBe(t);
    }
  });

  it("as ilustrações das duas versões estão em public/arte", () => {
    for (const t of all) {
      for (const v of ["clara", "escura"] as const) {
        expect(existsSync(join(process.cwd(), "public", illustrationUrl(t, v))), `${t.id} ${v}`).toBe(true);
      }
    }
  });

  it("a linha editável cabe no máximo do catalog, e o painel só oferece as com texto do cliente", () => {
    for (const t of all) {
      for (const l of t.linhas.filter((x) => x.editavel))
        expect(l.editavel!.maxLength).toBeLessThanOrEqual(40);
    }
    const offered = ILLUSTRATED_OPTIONS.flatMap(([, options]) => options.map(([id]) => id));
    expect(offered).toContain("criancas:aniversario-dino");
    expect(offered).toContain("pet-maes:mae-de-caramelo");
    expect(offered).not.toContain("gamer:player-1"); // sem linha editável
    expect(all.every((t) => t.linhas.some((l) => l.editavel))).toBe(true); // só entram as artes com texto do cliente
  });

  it("a versão vem da cor da peça", () => {
    expect(variantFor("Branco")).toBe("clara");
    expect(variantFor(undefined)).toBe("clara");
    expect(variantFor("Preto")).toBe("escura");
    expect(variantFor("Vinho")).toBe("escura");
    expect(variantFor("Azul Marinho")).toBe("escura");
    expect(variantFor("Vermelho")).toBe("vermelha");
  });

  it("o texto do cliente entra na linha editável e só nela", () => {
    const t = resolveIllustrated("criancas:body-pacotinho-titia")!;
    const lines = linesWithText(t, "da tia Lu");
    expect(lines.filter((l) => l.t === "da tia Lu")).toHaveLength(1);
    expect(lines.find((l) => l.k === "kicker")?.t).toBe("PACOTINHO DE AMOR");
    expect(linesWithText(t, "")).toEqual(t.linhas);
  });
});
