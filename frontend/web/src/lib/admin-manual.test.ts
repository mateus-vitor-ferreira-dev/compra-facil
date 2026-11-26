import { describe, expect, it } from "vitest";
import { ADMIN_SECTIONS } from "@/components/admin/admin-sections";
import { SCREEN_PURPOSE } from "./admin-manual";

describe("manual do painel", () => {
  it("toda tela do menu tem a frase dela no manual, e nenhuma frase sobra", () => {
    expect(Object.keys(SCREEN_PURPOSE).sort()).toEqual(ADMIN_SECTIONS.map((s) => s.href).sort());
  });
});
