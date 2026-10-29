import { addBusinessDays, arrival, businessDays, promised } from "./shipping";

// quinta-feira, 29 de outubro de 2026, 15h em Brasília
const NOW = new Date("2026-10-29T18:00:00Z");

describe("prazo em dias úteis", () => {
  it("pula o fim de semana", () => {
    expect(addBusinessDays(1, NOW).toISOString().slice(0, 10)).toBe("2026-10-30");
    expect(addBusinessDays(2, NOW).toISOString().slice(0, 10)).toBe("2026-11-02");
  });

  it("conta do dia em Brasília, mesmo de madrugada em UTC", () => {
    // sexta 23h em Brasília é sábado em UTC: o próximo dia útil é segunda
    expect(addBusinessDays(1, new Date("2026-10-31T02:00:00Z")).toISOString().slice(0, 10)).toBe(
      "2026-11-02",
    );
  });

  it("mostra as datas em que chega, no mesmo mês ou atravessando", () => {
    expect(arrival({ min_days: 6, max_days: 10 }, NOW)).toBe("chega entre 6 e 12 de nov.");
    expect(arrival({ min_days: 13, max_days: 25 }, NOW)).toBe("chega entre 17 de nov. e 3 de dez.");
    expect(arrival({ min_days: 1, max_days: 1 }, NOW)).toBe("chega em 30 de out.");
    expect(businessDays({ min_days: 13, max_days: 21 })).toBe("13 a 21 dias úteis");
    expect(promised("2026-11-27")).toBe("27 de nov.");
  });
});
