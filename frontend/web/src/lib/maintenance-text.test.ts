import { dayLabel, untilLabel, windowLabel } from "./maintenance-text";

// 3/11/2026 às 15:00 em Brasília
const NOW = Date.parse("2026-11-03T18:00:00Z");

describe("textos da manutenção (#370)", () => {
  it("diz o dia em Brasília: hoje, amanhã ou a data", () => {
    expect(dayLabel(Date.parse("2026-11-04T01:00:00Z"), NOW)).toBe("hoje"); // 22:00 de 3/11 em Brasília
    expect(dayLabel(Date.parse("2026-11-04T15:00:00Z"), NOW)).toBe("amanhã");
    expect(dayLabel(Date.parse("2026-11-10T15:00:00Z"), NOW)).toBe("10 de nov.");
  });

  it("descreve a janela, e a que atravessa a meia-noite leva os dois dias", () => {
    expect(windowLabel("2026-11-04T01:00:00Z", "2026-11-04T01:05:00Z", NOW)).toBe("hoje, das 22:00 às 22:05");
    expect(windowLabel("2026-11-04T02:30:00Z", "2026-11-04T03:30:00Z", NOW)).toBe(
      "hoje às 23:30 até amanhã às 00:30",
    );
    expect(untilLabel("2026-11-04T01:05:00Z")).toBe("até 22:05");
  });
});
