import { marginPeriod, presets } from "./margin-period";

describe("marginPeriod (#282)", () => {
  it("transforma os dias de Brasília no intervalo da API, com o último dia inteiro", () => {
    expect(marginPeriod({ de: "2026-11-01", ate: "2026-11-03" })).toMatchObject({
      from: "2026-11-01T03:00:00.000Z",
      to: "2026-11-04T03:00:00.000Z",
    });
  });

  it("aceita só um dos lados, e nenhum", () => {
    expect(marginPeriod({ de: "2026-11-01" })).toMatchObject({
      from: "2026-11-01T03:00:00.000Z",
      to: undefined,
    });
    const all = marginPeriod({});
    expect([all.from, all.to, all.error]).toEqual([undefined, undefined, undefined]);
  });

  it("recusa data inválida e período ao contrário", () => {
    expect(marginPeriod({ de: "2026-02-31" }).error).toBe("Use datas válidas, como 01/11/2026.");
    expect(marginPeriod({ de: "ontem" }).error).toBe("Use datas válidas, como 01/11/2026.");
    expect(marginPeriod({ de: "2026-11-03", ate: "2026-11-01" }).error).toBe(
      "O início do período vem antes do fim.",
    );
  });

  it("os atalhos contam o dia de hoje em Brasília", () => {
    // 3/11 às 01:00 em UTC ainda é 2/11 em Brasília
    const [week, month, current] = presets(Date.parse("2026-11-03T01:00:00Z"));
    expect(week).toMatchObject({ de: "2026-10-27", ate: "2026-11-02" });
    expect(month).toMatchObject({ de: "2026-10-04", ate: "2026-11-02" });
    expect(current).toMatchObject({ de: "2026-11-01", ate: "2026-11-02" });
  });
});
