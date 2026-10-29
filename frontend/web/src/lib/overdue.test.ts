import { lateBy } from "./overdue";

describe("lateBy", () => {
  const due = "2026-10-29T12:00:00Z";
  const after = (minutes: number) => Date.parse(due) + minutes * 60_000;

  it("diz o atraso na maior unidade que couber", () => {
    expect(lateBy(due, after(0))).toBe("há 1 minuto");
    expect(lateBy(due, after(45))).toBe("há 45 minutos");
    expect(lateBy(due, after(5 * 60))).toBe("há 5 horas");
    expect(lateBy(due, after(47 * 60))).toBe("há 47 horas");
    expect(lateBy(due, after(3 * 24 * 60 + 30))).toBe("há 3 dias");
  });
});
