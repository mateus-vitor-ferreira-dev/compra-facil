import { cookies } from "next/headers";
import { cookieJar, redirectOf, Redirected } from "@/test/next-mocks";
import { confirmSetup, startSetup } from "./actions";

vi.mock("next/headers", () => ({ cookies: vi.fn() }));
vi.mock("next/navigation", () => ({
  redirect: (to: string) => {
    throw new Redirected(to);
  },
}));

const pending = (status = "mfa_setup_required") =>
  Buffer.from(JSON.stringify({ ticket: "tiquete-setup", status, back: "/admin" })).toString("base64url");

const form = (fields: Record<string, string> = {}) => {
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

const KEY = "jbsw y3dp ehpk 3pxp";
const URI = "otpauth://totp/Embrulhado:admin%40exemplo.com?secret=JBSWY3DPEHPK3PXP&issuer=Embrulhado";

afterEach(() => vi.unstubAllGlobals());

describe("startSetup", () => {
  it("devolve a chave e o QR code em SVG, gerado aqui (sem serviço de fora)", async () => {
    withCookies({ cf_mfa: pending() });
    const calls = identity(() => Response.json({ shared_key: KEY, authenticator_uri: URI }));

    const state = await startSetup({}, form());

    expect(calls).toEqual([
      { url: "http://localhost/identity/v1/sessions/mfa/setup", body: { ticket: "tiquete-setup" } },
    ]);
    expect(state.key).toBe(KEY);
    expect(state.qr).toMatch(/^<svg[^>]*xmlns="http:\/\/www.w3.org\/2000\/svg"/);
    expect(state.qr).not.toMatch(/https?:\/\/(?!www\.w3\.org)/); // nenhuma URL externa no SVG
  });

  it("tíquete do login (não do cadastro) volta para a senha", async () => {
    withCookies({ cf_mfa: pending("mfa_required") });
    const calls = identity(() => Response.json({}));

    expect(await redirectOf(() => startSetup({}, form()))).toBe("/entrar?erro=verificacao-expirada");
    expect(calls).toHaveLength(0);
  });
});

describe("confirmSetup", () => {
  it("o primeiro código liga a 2FA: sessão, o tíquete some e os códigos vão para a tela seguinte", async () => {
    const jar = withCookies({ cf_mfa: pending() });
    const codes = Array.from({ length: 10 }, (_, i) => `abcd${i}-efgh${i}`);
    const calls = identity(() =>
      Response.json({
        access_token: "jwt-mfa",
        token_type: "Bearer",
        expires_in: 900,
        refresh_token: "opaco",
        refresh_expires_in: 2592000,
        recovery_codes: codes,
      }),
    );

    const to = await redirectOf(() => confirmSetup({}, form({ code: "123456" })));

    expect(to).toBe("/entrar/codigos-de-recuperacao");
    expect(calls[0]).toEqual({
      url: "http://localhost/identity/v1/sessions/mfa/setup/confirm",
      body: { ticket: "tiquete-setup", code: "123456" },
    });
    expect(jar.set.map((c) => c.name)).toEqual(["cf_access", "cf_refresh", "cf_mfa_codes"]);
    const saved = jar.set[2]!;
    expect(saved.options).toMatchObject({
      httpOnly: true,
      path: "/entrar/codigos-de-recuperacao",
      maxAge: 600,
    });
    expect(JSON.parse(Buffer.from(saved.value, "base64url").toString())).toEqual({ codes, back: "/admin" });
    expect(jar.deleted).toEqual([{ name: "cf_mfa", path: "/entrar" }]);
  });

  it("código errado fica na tela, sem sessão", async () => {
    const jar = withCookies({ cf_mfa: pending() });
    identity(() => Response.json({ status: 422 }, { status: 422 }));

    const state = await confirmSetup({}, form({ code: "000000" }));

    expect(state.error).toMatch(/Código incorreto/);
    expect(jar.set).toEqual([]);
  });
});
