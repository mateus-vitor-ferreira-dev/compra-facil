const load = async (vars: Record<string, string | undefined>) => {
  vi.resetModules();
  for (const [k, v] of Object.entries(vars)) vi.stubEnv(k, v as string);
  return (await import("./gateway-headers")).gatewayHeaders;
};

const KEY = "segredo-do-bff-com-16+";

afterEach(() => vi.unstubAllEnvs());

describe("gatewayHeaders", () => {
  it("manda o IP do comprador e o segredo, com os dois configurados", async () => {
    const gatewayHeaders = await load({ BFF_GATEWAY_KEY: KEY, CLIENT_IP_HEADER: "x-real-ip" });
    expect(gatewayHeaders(new Headers({ "x-real-ip": "203.0.113.7" }))).toEqual({
      "X-Bff-Key": KEY,
      "X-Client-IP": "203.0.113.7",
    });
  });

  it("de uma lista (x-forwarded-for), usa o primeiro", async () => {
    const gatewayHeaders = await load({ BFF_GATEWAY_KEY: KEY, CLIENT_IP_HEADER: "x-forwarded-for" });
    expect(gatewayHeaders(new Headers({ "x-forwarded-for": "2001:db8::1, 10.0.0.1" }))).toMatchObject({
      "X-Client-IP": "2001:db8::1",
    });
  });

  it("sem configuração, sem o cabeçalho ou com valor que não é IP, não manda nada", async () => {
    expect((await load({}))(new Headers({ "x-real-ip": "203.0.113.7" }))).toEqual({});
    const configured = await load({ BFF_GATEWAY_KEY: KEY, CLIENT_IP_HEADER: "x-real-ip" });
    expect(configured(new Headers())).toEqual({});
    expect(configured(new Headers({ "x-real-ip": "203.0.113.7; 198.51.100.1" }))).toEqual({});
    expect(configured(new Headers({ "x-real-ip": "nao-e-ip" }))).toEqual({});
  });

  it("ignora outro cabeçalho de IP que o cliente possa mandar", async () => {
    const gatewayHeaders = await load({ BFF_GATEWAY_KEY: KEY, CLIENT_IP_HEADER: "x-real-ip" });
    expect(gatewayHeaders(new Headers({ "x-forwarded-for": "198.51.100.1" }))).toEqual({});
  });
});
