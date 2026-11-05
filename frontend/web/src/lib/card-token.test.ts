import { tokenizeCard, type PaymentProvider } from "./card-token";

const efi = vi.hoisted(() => ({ result: vi.fn(), data: vi.fn(), account: vi.fn(), environment: vi.fn() }));

// a biblioteca da Efí só fala com os hosts dela: aqui, a cadeia de chamadas dela, gravada
vi.mock("payment-token-efi", () => {
  const chain = {
    setAccount: (v: string) => (efi.account(v), chain),
    setEnvironment: (v: string) => (efi.environment(v), chain),
    setCreditCardData: (v: unknown) => (efi.data(v), chain),
    getPaymentToken: () => efi.result(),
  };
  return { default: { CreditCard: chain } };
});

const simulator: PaymentProvider = {
  environment: "simulator",
  payeeCode: "0123456789abcdef0123456789abcdef",
  url: "http://localhost:8092",
};
const card = {
  number: "5502093788528294",
  name: "Ana Souza",
  month: 3,
  year: 2030,
  cvv: "123",
  brand: "master" as const,
};

const reply = (status: number, body: unknown) =>
  vi.spyOn(globalThis, "fetch").mockResolvedValue(new Response(JSON.stringify(body), { status }));

afterEach(() => vi.restoreAllMocks());

describe("tokenizeCard na Efí", () => {
  it("usa a biblioteca com o identificador de conta e devolve só o token e a bandeira", async () => {
    efi.result.mockResolvedValue({ payment_token: "a".repeat(40), card_mask: "XXXXXXXXXXXX8294" });
    const result = await tokenizeCard({ ...simulator, environment: "production" }, card);
    expect(result).toEqual({ ok: true, token: "a".repeat(40), brand: "master" });
    expect(efi.account).toHaveBeenCalledWith("0123456789abcdef0123456789abcdef");
    expect(efi.environment).toHaveBeenCalledWith("production");
    expect(efi.data).toHaveBeenCalledWith({
      brand: "mastercard",
      number: "5502093788528294",
      cvv: "123",
      expirationMonth: "03",
      expirationYear: "2030",
      holderName: "Ana Souza",
      holderDocument: "",
      reuse: false,
    });
  });

  it("o erro de validação da biblioteca vai para o campo certo", async () => {
    efi.result.mockRejectedValue({
      code: 400,
      error: "invalid_card_number",
      error_description: "Número inválido",
    });
    expect(await tokenizeCard({ ...simulator, environment: "sandbox" }, card)).toMatchObject({
      ok: false,
      field: "number",
    });
    expect(efi.environment).toHaveBeenCalledWith("sandbox");
  });

  it("sem rede: o erro diz para tentar de novo", async () => {
    efi.result.mockRejectedValue(new TypeError("failed to fetch"));
    expect(await tokenizeCard({ ...simulator, environment: "production" }, card)).toMatchObject({
      ok: false,
      message: expect.stringContaining("Tente de novo"),
    });
  });
});

describe("tokenizeCard no simulador", () => {
  it("manda o cartão ao payment-sim no formato da biblioteca", async () => {
    const fetch = reply(200, {
      code: 200,
      data: { payment_token: "b".repeat(40), card_mask: "XXXXXXXXXXXX8294" },
    });
    expect(await tokenizeCard(simulator, card)).toEqual({ ok: true, token: "b".repeat(40), brand: "master" });
    const [url, init] = fetch.mock.calls[0] ?? [];
    expect(String(url)).toBe("http://localhost:8092/sim/card-token");
    expect(JSON.parse(String(init?.body))).toMatchObject({
      payee_code: "0123456789abcdef0123456789abcdef",
      brand: "mastercard",
      expiration_month: "03",
      expiration_year: "2030",
    });
  });

  it("o dado recusado vira o erro do campo", async () => {
    reply(400, {
      code: 3500060,
      error: "invalid_expiration_month",
      error_description: "O mês de vencimento é inválido.",
    });
    expect(await tokenizeCard(simulator, card)).toMatchObject({ ok: false, field: "expiry" });
    reply(400, {
      code: 3500011,
      error: "invalid_data",
      error_description: "Cvv deve conter entre 3 e 4 caracteres",
    });
    expect(await tokenizeCard(simulator, card)).toMatchObject({ ok: false, field: "cvv" });
  });

  it("provedor fora: o erro diz que nada foi cobrado", async () => {
    vi.spyOn(globalThis, "fetch").mockRejectedValue(new TypeError("failed to fetch"));
    expect(await tokenizeCard(simulator, card)).toMatchObject({
      ok: false,
      message: expect.stringContaining("Tente de novo"),
    });
    reply(500, {});
    expect(await tokenizeCard(simulator, card)).toMatchObject({
      ok: false,
      message: expect.stringContaining("Nada foi cobrado"),
    });
  });
});
