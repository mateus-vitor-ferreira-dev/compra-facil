import { cookies } from "next/headers";
import { cookieJar, redirectOf, Redirected } from "@/test/next-mocks";
import { verifyMfa } from "./actions";

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({
  redirect: (to: string) => {
    throw new Redirected(to);
  },
}));

const pending = (status = "mfa_required", back = "/admin/pedidos") =>
  Buffer.from(JSON.stringify({ ticket: "tiquete-1", status, back })).toString("base64url");

const form = (fields: Record<string, string>) => {
  const data = new FormData();
  for (const [k, v] of Object.entries(fields)) data.set(k, v);
  return data;
};

function withCookies(initial: Record<string, string>) {
  const jar = cookieJar(initial);
  vi.mocked(cookies).mockResolvedValue(jar.store as never);
  return jar;
}

function identity(response: () => Response) {
  const calls: { url: string; body: unknown }[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      calls.push({ url: request.url, body: await request.clone().json() });
      return response();
    }),
  );
  return calls;
}

const tokens = {
  access_token: "jwt-mfa",
  token_type: "Bearer",
  expires_in: 900,
  refresh_token: "opaco",
  refresh_expires_in: 2592000,
};

afterEach(() => vi.unstubAllGlobals());

describe("verifyMfa", () => {
  it("o código certo vira a sessão, apaga o tíquete e volta para onde a pessoa ia", async () => {
    const jar = withCookies({ cf_mfa: pending() });
    const calls = identity(() => Response.json(tokens));

    const to = await redirectOf(() => verifyMfa({}, form({ modo: "app", code: "123 456" })));

    expect(to).toBe("/admin/pedidos");
    expect(calls).toEqual([
      { url: "http://localhost/identity/v1/sessions/mfa", body: { ticket: "tiquete-1", code: "123456" } },
    ]);
    expect(jar.set.map((c) => c.name)).toEqual(["cf_access", "cf_refresh"]);
    expect(jar.set[0]?.options).toMatchObject({ httpOnly: true });
    expect(jar.deleted).toEqual([{ name: "cf_mfa", path: "/entrar" }]);
  });

  it("o código de recuperação vai no campo dele, normalizado", async () => {
    withCookies({ cf_mfa: pending() });
    const calls = identity(() => Response.json(tokens));

    await redirectOf(() => verifyMfa({}, form({ modo: "recuperacao", recovery_code: " K7M2P 9XQ4R " })));

    expect(calls[0]?.body).toEqual({ ticket: "tiquete-1", recovery_code: "k7m2p-9xq4r" });
  });

  it("código fora do formato não chama o identity", async () => {
    withCookies({ cf_mfa: pending() });
    const calls = identity(() => Response.json(tokens));

    const state = await verifyMfa({}, form({ modo: "app", code: "12345" }));

    expect(state.error).toMatch(/6 números/);
    expect(calls).toHaveLength(0);
  });

  it("código errado (422) fica na tela; o tíquete continua", async () => {
    const jar = withCookies({ cf_mfa: pending() });
    identity(() => Response.json({ status: 422 }, { status: 422 }));

    const state = await verifyMfa({}, form({ modo: "app", code: "000000" }));

    expect(state.error).toMatch(/Código incorreto/);
    expect(jar.deleted).toEqual([]);
  });

  it("bloqueio (423) diz quanto falta", async () => {
    withCookies({ cf_mfa: pending() });
    identity(() => new Response(null, { status: 423, headers: { "Retry-After": "600" } }));

    expect((await verifyMfa({}, form({ modo: "app", code: "000000" }))).error).toMatch(/em 10 min/);
  });

  it("tíquete vencido ou usado (401): apaga e volta para a senha", async () => {
    const jar = withCookies({ cf_mfa: pending() });
    identity(() => new Response(null, { status: 401 }));

    const to = await redirectOf(() => verifyMfa({}, form({ modo: "app", code: "123456" })));

    expect(to).toBe("/entrar?erro=verificacao-expirada");
    expect(jar.deleted).toEqual([{ name: "cf_mfa", path: "/entrar" }]);
  });

  it("sem o cookie do tíquete (ou com o do cadastro), volta para a senha sem chamar o identity", async () => {
    const calls = identity(() => Response.json(tokens));

    withCookies({});
    expect(await redirectOf(() => verifyMfa({}, form({ code: "123456" })))).toBe(
      "/entrar?erro=verificacao-expirada",
    );
    withCookies({ cf_mfa: pending("mfa_setup_required") });
    expect(await redirectOf(() => verifyMfa({}, form({ code: "123456" })))).toBe(
      "/entrar?erro=verificacao-expirada",
    );
    withCookies({ cf_mfa: "lixo" });
    expect(await redirectOf(() => verifyMfa({}, form({ code: "123456" })))).toBe(
      "/entrar?erro=verificacao-expirada",
    );
    expect(calls).toHaveLength(0);
  });

  it("um destino de fora da loja no cookie não vira redirect aberto", async () => {
    withCookies({ cf_mfa: pending("mfa_required", "https://outro.site") });
    identity(() => Response.json(tokens));

    expect(await redirectOf(() => verifyMfa({}, form({ code: "123456" })))).toBe("/conta");
  });
});
