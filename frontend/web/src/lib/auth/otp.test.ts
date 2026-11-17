import { normalizeOtp, normalizeRecoveryCode } from "./otp";

describe("normalizeOtp", () => {
  it.each([
    ["123456", "123456"],
    ["123 456", "123456"],
    [" 123-456 ", "123456"],
  ])("%j vira %j", (input, expected) => expect(normalizeOtp(input)).toBe(expected));

  it.each(["12345", "1234567", "12a456", "", null, 123456])("%j não é código", (input) =>
    expect(normalizeOtp(input)).toBeNull(),
  );
});

describe("normalizeRecoveryCode", () => {
  it("aceita com ou sem hífen, em qualquer caixa", () => {
    expect(normalizeRecoveryCode("k7m2p-9xq4r")).toBe("k7m2p-9xq4r");
    expect(normalizeRecoveryCode("K7M2P9XQ4R")).toBe("k7m2p-9xq4r");
    expect(normalizeRecoveryCode(" k7m2p 9xq4r ")).toBe("k7m2p-9xq4r");
  });

  it.each(["curto", "k7m2p-9xq4r-a", "k7m2p_9xq4r", "x".repeat(40), undefined])("%j não é código", (input) =>
    expect(normalizeRecoveryCode(input)).toBeNull(),
  );
});
