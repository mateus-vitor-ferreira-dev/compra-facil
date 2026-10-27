import { campaignPhase, fromLocalInput, toLocalInput } from "./campaign-time";

describe("período da campanha em Brasília", () => {
  it("ida e volta entre o campo e a API", () => {
    expect(fromLocalInput("2026-11-27T00:00")).toBe("2026-11-27T03:00:00.000Z");
    expect(toLocalInput("2026-11-27T03:00:00Z")).toBe("2026-11-27T00:00");
    expect(toLocalInput("2026-12-01T02:59:00Z")).toBe("2026-11-30T23:59");
  });

  it("o que não é data vira nulo", () => {
    expect(fromLocalInput("")).toBeNull();
    expect(fromLocalInput("27/11/2026")).toBeNull();
  });

  it("a fase pelo relógio", () => {
    const c = { starts_at: "2026-11-27T03:00:00Z", ends_at: "2026-11-30T03:00:00Z" };
    expect(campaignPhase(c, Date.parse("2026-11-26T00:00:00Z"))).toBe("scheduled");
    expect(campaignPhase(c, Date.parse("2026-11-28T00:00:00Z"))).toBe("running");
    expect(campaignPhase(c, Date.parse("2026-11-30T03:00:00Z"))).toBe("ended");
  });
});
