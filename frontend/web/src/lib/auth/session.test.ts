import {
  ACCESS_COOKIE,
  REFRESH_COOKIE,
  adminWithoutMfa,
  clearedCookies,
  refreshSession,
  sessionCookies,
  type Tokens,
} from "./session";

const tokens: Tokens = {
  access_token: "jwt",
  token_type: "Bearer",
  expires_in: 900,
  refresh_token: "opaco",
  refresh_expires_in: 2592000,
};

describe("cookies da sessão", () => {
  it("httpOnly e SameSite=Lax; o access vence 1 min antes do token", () => {
    const [access, refresh] = sessionCookies(tokens);

    expect(access).toMatchObject({ name: ACCESS_COOKIE, value: "jwt" });
    expect(access?.options).toMatchObject({ httpOnly: true, sameSite: "lax", path: "/", maxAge: 840 });
    expect(refresh).toMatchObject({ name: REFRESH_COOKIE, value: "opaco" });
    expect(refresh?.options.maxAge).toBe(2592000);
  });

  it("logout apaga os dois", () => {
    expect(clearedCookies().map((c) => [c.name, c.options.maxAge])).toEqual([
      [ACCESS_COOKIE, 0],
      [REFRESH_COOKIE, 0],
    ]);
  });
});

describe("adminWithoutMfa", () => {
  const jwt = (payload: object) =>
    `eyJhbGciOiJSUzI1NiJ9.${Buffer.from(JSON.stringify(payload)).toString("base64url")}.assinatura`;

  it("operador sem amr (sessão de antes da verificação em duas etapas) precisa entrar de novo", () => {
    expect(adminWithoutMfa(jwt({ roles: ["admin"] }))).toBe(true);
    expect(adminWithoutMfa(jwt({ roles: "admin", amr: ["pwd"] }))).toBe(true);
  });

  it("operador com amr mfa, cliente, auditor e token ilegível seguem", () => {
    expect(adminWithoutMfa(jwt({ roles: ["admin"], amr: ["mfa"], name: "José Conceição" }))).toBe(false);
    expect(adminWithoutMfa(jwt({ roles: ["customer"] }))).toBe(false);
    expect(adminWithoutMfa(jwt({ roles: ["auditor"] }))).toBe(false);
    expect(adminWithoutMfa("nao-e-jwt")).toBe(false);
  });
});

describe("refreshSession", () => {
  it("requisições juntas com o mesmo refresh token fazem um refresh só (o identity derrubaria a sessão)", async () => {
    let calls = 0;
    const fetcher = vi.fn(async () => {
      calls++;
      await new Promise((r) => setTimeout(r, 20));
      return Response.json({ ...tokens, access_token: "novo" });
    }) as unknown as typeof fetch;

    const results = await Promise.all([1, 2, 3].map(() => refreshSession("rt-concorrente", fetcher)));

    expect(calls).toBe(1);
    expect(results.every((r) => r.ok && r.tokens.access_token === "novo")).toBe(true);
  });

  it("refresh recusado devolve o status", async () => {
    const fetcher = vi.fn(async () => new Response(null, { status: 401 })) as unknown as typeof fetch;

    await expect(refreshSession("rt-revogado", fetcher)).resolves.toEqual({ ok: false, status: 401 });
  });
});
