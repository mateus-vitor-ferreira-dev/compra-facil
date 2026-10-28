const load = async (vars: Record<string, string | undefined>) => {
  vi.resetModules();
  for (const [k, v] of Object.entries(vars)) vi.stubEnv(k, v as string);
  return (await import("./gateway-headers")).gatewayHeaders;
};

const KEY = "segredo-do-bff-com-16+";

afterEach(() => vi.unstubAllEnvs());

describe("isIp", () => {
  it("aceita IPv4 e IPv6 válidos", async () => {
    const { isIp } = await import("./gateway-headers");
    for (const ip of [
      "203.0.113.7",
      "0.0.0.0",
      "255.255.255.255",
      "2001:db8::1",
      "::1",
      "::ffff:192.0.2.1",
    ]) {
      expect(isIp(ip), ip).toBe(true);
    }
  });

  it("recusa o resto", async () => {
    const { isIp } = await import("./gateway-headers");
    for (const ip of [
      "256.1.1.1",
      "1.2.3",
      "01.2.3.4",
      "1:::2",
      "2001:db8::g",
      "fe80::1%eth0",
      "[::1]",
      "",
      "a.b.c.d",
    ]) {
      expect(isIp(ip), ip).toBe(false);
    }
  });
});

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
