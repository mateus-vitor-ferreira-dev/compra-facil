import { POST } from "./route";

const login = (body: unknown) =>
  new Request("http://localhost:3000/api/auth/login", {
    method: "POST",
    headers: { "content-type": "application/json" },
    body: JSON.stringify(body),
  });

afterEach(() => vi.unstubAllGlobals());

describe("POST /api/auth/login", () => {
  it("vira cookies httpOnly e não devolve os tokens no corpo", async () => {
    const fetchMock = vi.fn(async (_input: RequestInfo | URL) =>
      Response.json({
        access_token: "jwt",
        token_type: "Bearer",
        expires_in: 900,
        refresh_token: "opaco",
        refresh_expires_in: 2592000,
      }),
    );
    vi.stubGlobal("fetch", fetchMock);

    const res = await POST(login({ email: "ana@exemplo.com", password: "senha-boa-123" }));

    expect(res.status).toBe(204);
    const cookies = res.headers.getSetCookie();
    expect(cookies.some((c) => c.startsWith("cf_access=jwt") && /HttpOnly/i.test(c))).toBe(true);
    expect(cookies.some((c) => c.startsWith("cf_refresh=opaco") && /SameSite=lax/i.test(c))).toBe(true);
    expect(await res.text()).toBe("");
    const called = fetchMock.mock.calls[0]?.[0] as unknown as Request;
    expect(called.url).toBe("http://localhost/identity/v1/sessions");
  });

  it("repassa o ProblemDetails do identity (ex.: 423 com Retry-After)", async () => {
    vi.stubGlobal(
      "fetch",
      vi.fn(async () =>
        Response.json(
          { type: "about:blank", title: "Conta bloqueada", status: 423 },
          { status: 423, headers: { "Content-Type": "application/problem+json", "Retry-After": "900" } },
        ),
      ),
    );

    const res = await POST(login({ email: "ana@exemplo.com", password: "errada" }));

    expect(res.status).toBe(423);
    expect(res.headers.get("retry-after")).toBe("900");
    expect((await res.json()).title).toBe("Conta bloqueada");
    expect(res.headers.getSetCookie()).toEqual([]);
  });

  it("corpo inválido é 400 sem chamar o identity", async () => {
    const fetchMock = vi.fn();
    vi.stubGlobal("fetch", fetchMock);

    const res = await POST(login({ email: 123 }));

    expect(res.status).toBe(400);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});
