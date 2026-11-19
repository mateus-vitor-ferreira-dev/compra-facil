import { describe, expect, it } from "vitest";
import { crc32, withDpi } from "./png";
import { splitLines } from "./render";
import { TEMPLATE_OPTIONS, artFileName, guessPiece, resolveTemplate } from "./templates";

// um PNG de 1×1 transparente (o que o canvas gera, sem pHYs)
const tinyPng = Uint8Array.from(
  atob("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNkYPhfDwAChwGA60e6kgAAAABJRU5ErkJggg=="),
  (c) => c.charCodeAt(0),
);

const chunks = (png: Uint8Array) => {
  const view = new DataView(png.buffer, png.byteOffset, png.byteLength);
  const out: { type: string; data: Uint8Array }[] = [];
  for (let at = 8; at + 8 <= png.length;) {
    const length = view.getUint32(at);
    out.push({
      type: String.fromCharCode(...png.subarray(at + 4, at + 8)),
      data: png.subarray(at + 8, at + 8 + length),
    });
    at += 12 + length;
  }
  return out;
};

describe("arte: o PNG de impressão", () => {
  it("crc32 confere com o valor conhecido", () => {
    expect(crc32(new TextEncoder().encode("123456789"))).toBe(0xcbf43926);
  });

  it("põe o pHYs de 300 DPI logo depois do IHDR, uma vez só", () => {
    const once = withDpi(tinyPng);
    const twice = withDpi(once);
    const types = chunks(twice).map((c) => c.type);
    expect(types).toEqual(["IHDR", "pHYs", "IDAT", "IEND"]);
    const phys = new DataView(chunks(twice)[1]!.data.buffer, chunks(twice)[1]!.data.byteOffset);
    expect(phys.getUint32(0)).toBe(11811); // 300 / 0,0254
    expect(phys.getUint32(4)).toBe(11811);
  });

  it("recusa o que não é PNG", () => {
    expect(() => withDpi(new Uint8Array(40))).toThrow("não é um PNG");
  });
});

describe("arte: os modelos", () => {
  it("resolve a base e a variante, e recusa o id que não existe", () => {
    expect(resolveTemplate("nome-classico:mae")).toMatchObject({
      base: "nome-classico",
      kicker: "MÃE DE",
      hat: false,
    });
    expect(resolveTemplate("familia-sobrenome:natal")).toMatchObject({
      kicker: "FAMÍLIA",
      footer: "NATAL 2026",
      hat: true,
    });
    expect(resolveTemplate("caneca-nome")).toMatchObject({ twoSides: true, kicker: undefined });
    expect(resolveTemplate("nome-classico:nao-existe")).toBeUndefined();
    expect(resolveTemplate("sumiu")).toBeUndefined();
  });

  it("toda opção do painel resolve e segue o formato que o catalog aceita", () => {
    for (const [id] of TEMPLATE_OPTIONS) {
      expect(resolveTemplate(id), id).toBeDefined();
      expect(id).toMatch(/^[a-z0-9]+(-[a-z0-9]+)*(:[a-z0-9]+(-[a-z0-9]+)*)?$/);
      expect(id.length).toBeLessThanOrEqual(40);
    }
  });

  it("adivinha a peça pelo nome do item", () => {
    expect(guessPiece("caneca-nome", "Caneca Mãe")).toBe("caneca");
    expect(guessPiece("nome-classico", "Body Meu Primeiro Natal")).toBe("body");
    expect(guessPiece("nome-classico", "Camisa Pet Noel")).toBe("pet");
    expect(guessPiece("nome-classico", "Camiseta Infantil Noelzinho")).toBe("infantil");
    expect(guessPiece("nome-classico:mae", "Camiseta Mãe de")).toBe("adulto");
  });

  it("quebra o texto longo em duas linhas no espaço do meio", () => {
    expect(splitLines("Ana")).toEqual(["Ana"]);
    expect(splitLines("Maria Eduarda Souza")).toEqual(["Maria Eduarda", "Souza"]);
    expect(splitLines("Desde 2019")).toEqual(["Desde 2019"]);
  });

  it("dá ao arquivo um nome sem acento nem espaço", () => {
    expect(artFileName("0193a2b4-0000-7000-8000-000000000000", "adulto", "João & Maria")).toBe(
      "arte-0193a2b4-adulto-joao-maria.png",
    );
  });
});
