import { rejectCrossSite } from "./request";

const post = (headers: Record<string, string>) =>
  new Request("http://localhost:3000/api/auth/login", { method: "POST", headers, body: "{}" });

describe("rejectCrossSite", () => {
  it("aceita JSON do próprio site (com ou sem Origin)", () => {
    expect(rejectCrossSite(post({ "content-type": "application/json" }))).toBeNull();
    expect(
      rejectCrossSite(post({ "content-type": "application/json", origin: "http://localhost:3000" })),
    ).toBeNull();
  });

  it("recusa formulário (415) e outra origem (403)", () => {
    expect(rejectCrossSite(post({ "content-type": "application/x-www-form-urlencoded" }))?.status).toBe(415);
    expect(
      rejectCrossSite(post({ "content-type": "application/json", origin: "https://evil.example" }))?.status,
    ).toBe(403);
  });
});
