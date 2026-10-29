import { tokenizeCard } from "./card-token";

const provider = { url: "http://localhost:8092", publicKey: "TEST-sim-public-key" };
const card = {
  number: "4509953566233704",
  name: "APRO",
  month: 11,
  year: 2030,
  cvv: "123",
  brand: "visa" as const,
};

const reply = (status: number, body: unknown) =>
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify(body), { status }));

afterEach(() => vi.restoreAllMocks());

describe("tokenizeCard", () => {
  it("manda o cartão ao provedor com a chave pública e devolve só o token e a bandeira", async () => {
    const fetch = reply(201, { id: "tok-1", payment_method_id: "visa", last_four_digits: "3704" });
    expect(await tokenizeCard(provider, card)).toEqual({ ok: true, token: "tok-1", brand: "visa" });
    const [url, init] = fetch.mock.calls[0] ?? [];
    expect(String(url)).toBe("http://localhost:8092/v1/card_tokens?public_key=TEST-sim-public-key");
    expect(JSON.parse(String(init?.body))).toEqual({
      card_number: "4509953566233704",
      security_code: "123",
      expiration_month: 11,
      expiration_year: 2030,
      cardholder: { name: "APRO" },
    });
  });

  it("o dado recusado pelo provedor vira o erro do campo", async () => {
    reply(400, { message: "invalid card: validade", error: "invalid_card" });
    expect(await tokenizeCard(provider, card)).toMatchObject({ ok: false, field: "expiry" });
  });

  it("provedor fora: o erro diz que nada foi cobrado", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("failed to fetch"));
    expect(await tokenizeCard(provider, card)).toMatchObject({
      ok: false,
      message: expect.stringContaining("Tente de novo"),
    });
    reply(500, {});
    expect(await tokenizeCard(provider, card)).toMatchObject({
      ok: false,
      message: expect.stringContaining("Nada foi cobrado"),
    });
  });
});
