const loadGoogle = async (vars: Record<string, string>) => {
  vi.resetModules();
  for (const [k, v] of Object.entries(vars)) vi.stubEnv(k, v);
  return import("./google");
};

afterEach(() => vi.unstubAllEnvs());

describe("login com o Google", () => {
  it("o challenge S256 é o SHA-256 do verifier em base64url (RFC 7636)", async () => {
    const { codeChallenge } = await loadGoogle({});
    const verifier = "um-verifier-de-teste-com-43-caracteres-ou-mais";
    // cálculo independente, pela Web Crypto
    const digest = new Uint8Array(await crypto.subtle.digest("SHA-256", new TextEncoder().encode(verifier)));
    const expected = Buffer.from(digest).toString("base64url");
    expect(codeChallenge(verifier)).toBe(expected);
    expect(codeChallenge(verifier)).toMatch(/^[A-Za-z0-9_-]{43}$/);
  });

  it("sem o cliente OAuth configurado, o Google fica desligado", async () => {
    expect((await loadGoogle({})).googleEnabled()).toBe(false);
    expect((await loadGoogle({ GOOGLE_CLIENT_ID: "id", GOOGLE_CLIENT_SECRET: "s" })).googleEnabled()).toBe(
      true,
    );
  });

  it("a URL do Google leva state, nonce, PKCE e o callback da loja", async () => {
    const { startLogin, codeChallenge } = await loadGoogle({
      GOOGLE_CLIENT_ID: "loja.apps.googleusercontent.com",
      GOOGLE_CLIENT_SECRET: "segredo",
      SITE_URL: "https://loja.exemplo.com",
    });
    const { pending, url } = startLogin("/carrinho");
    const q = new URL(url).searchParams;
    expect(url.startsWith("https://accounts.google.com/o/oauth2/v2/auth?")).toBe(true);
    expect(q.get("client_id")).toBe("loja.apps.googleusercontent.com");
    expect(q.get("redirect_uri")).toBe("https://loja.exemplo.com/api/auth/google/callback");
    expect(q.get("scope")).toBe("openid email profile");
    expect(q.get("state")).toBe(pending.state);
    expect(q.get("nonce")).toBe(pending.nonce);
    expect(q.get("code_challenge")).toBe(codeChallenge(pending.verifier));
    expect(q.get("code_challenge_method")).toBe("S256");
    expect(url).not.toContain(pending.verifier); // o verifier não sai do servidor
    expect(pending.back).toBe("/carrinho");
    expect(startLogin("/").pending.state).not.toBe(pending.state);
  });

  it("cookie adulterado ou de outro formato não vira login", async () => {
    const { encodeCookie, readPending } = await loadGoogle({});
    const ok = { state: "s", verifier: "v", nonce: "n", back: "/" };
    expect(readPending(encodeCookie(ok))).toEqual(ok);
    expect(readPending(encodeCookie({ state: "s" }))).toBeUndefined();
    expect(readPending("%%%")).toBeUndefined();
    expect(readPending(undefined)).toBeUndefined();
  });

  it("lê o e-mail do ID token só para mostrar", async () => {
    const { emailOf } = await loadGoogle({});
    const payload = Buffer.from(JSON.stringify({ email: "ana@gmail.com" })).toString("base64url");
    expect(emailOf(`x.${payload}.y`)).toBe("ana@gmail.com");
    expect(emailOf("lixo")).toBe("");
  });
});
