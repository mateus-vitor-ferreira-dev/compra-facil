import { openGuestAccess } from "./flows";
import { ACCESS_COOKIE, REFRESH_COOKIE } from "./session";

/** Os cookies da requisição: o que o navegador mandou e o que a resposta grava. */
const jar = new Map<string, string>();
vi.mock("next/headers", () => ({
  cookies: async () => ({
    has: (name: string) => jar.has(name),
    get: (name: string) => (jar.has(name) ? { name, value: jar.get(name) } : undefined),
    set: (name: string, value: string) => (value ? jar.set(name, value) : jar.delete(name)),
  }),
}));

type Who = { id: string; email: string; is_guest: boolean };
const ANA: Who = { id: "0199f6a0-0000-7000-8000-000000000a1a", email: "ana@exemplo.com", is_guest: false };
const BIA: Who = {
  id: "0199f6a0-0000-7000-8000-000000000b1a",
  email: "convidada@exemplo.com",
  is_guest: true,
};
const CAIO: Who = { id: "0199f6a0-0000-7000-8000-000000000c1a", email: "caio@exemplo.com", is_guest: true };

const unauthorized = () => Response.json({ title: "Não autenticado", status: 401 }, { status: 401 });

/** O identity falso: o link "link-bom" é da Bia; cada access token diz de quem é a sessão. */
function identity(accounts: Record<string, Who>) {
  const revoked: string[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (input: RequestInfo | URL) => {
      const request = input as Request;
      const path = new URL(request.url).pathname;
      if (path.endsWith("/v1/sessions/guest/access")) {
        const { token } = (await request.json()) as { token: string };
        if (token !== "link-bom") return unauthorized();
        return Response.json({
          access_token: "access-bia",
          token_type: "Bearer",
          expires_in: 900,
          refresh_token: "refresh-bia",
          refresh_expires_in: 2592000,
        });
      }
      if (path.endsWith("/v1/sessions/revoke")) {
        revoked.push(((await request.json()) as { refresh_token: string }).refresh_token);
        return new Response(null, { status: 204 });
      }
      if (path.endsWith("/v1/me")) {
        const account = accounts[(request.headers.get("authorization") ?? "").replace("Bearer ", "")];
        if (!account) return unauthorized();
        return Response.json({
          ...account,
          name: "Fulano",
          email_confirmed: true,
          roles: ["customer"],
          created_at: "2026-10-01T10:00:00Z",
        });
      }
      return new Response(null, { status: 404 });
    }),
  );
  return revoked;
}

function signedIn(access: string, refresh: string) {
  jar.set(ACCESS_COOKIE, access);
  jar.set(REFRESH_COOKIE, refresh);
}

beforeEach(() => jar.clear());
afterEach(() => vi.unstubAllGlobals());

describe("openGuestAccess", () => {
  it("sem sessão no navegador, abre a do link", async () => {
    identity({ "access-bia": BIA });
    expect(await openGuestAccess("link-bom")).toEqual({ ok: true });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-bia");
  });

  it("com a conta com senha aberta, não a troca: revoga a sessão do link e pede para sair antes", async () => {
    signedIn("access-ana", "refresh-ana");
    const revoked = identity({ "access-ana": ANA, "access-bia": BIA });
    expect(await openGuestAccess("link-bom")).toEqual({
      ok: false,
      reason: "other",
      current: "account",
      email: "co***@exemplo.com",
    });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-ana");
    expect(revoked).toEqual(["refresh-bia"]);
  });

  it("com outra compra sem conta aberta, pede a confirmação com o e-mail mascarado do link", async () => {
    signedIn("access-caio", "refresh-caio");
    identity({ "access-caio": CAIO, "access-bia": BIA });
    expect(await openGuestAccess("link-bom")).toEqual({
      ok: false,
      reason: "other",
      current: "guest",
      email: "co***@exemplo.com",
    });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-caio");
  });

  it("a mesma compra já aberta: só renova a sessão", async () => {
    signedIn("access-bia-antiga", "refresh-bia-antiga");
    identity({ "access-bia-antiga": BIA, "access-bia": BIA });
    expect(await openGuestAccess("link-bom")).toEqual({ ok: true });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-bia");
  });

  it("a sessão que não se lê (vencida, revogada) conta como conta: o comprador decide sair", async () => {
    jar.set(REFRESH_COOKIE, "refresh-velho");
    identity({ "access-bia": BIA });
    expect(await openGuestAccess("link-bom")).toMatchObject({
      ok: false,
      reason: "other",
      current: "account",
    });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-velho");
  });

  it("confirmada a troca, a sessão de antes é revogada e a do link abre", async () => {
    signedIn("access-ana", "refresh-ana");
    const revoked = identity({ "access-ana": ANA, "access-bia": BIA });
    expect(await openGuestAccess("link-bom", true)).toEqual({ ok: true });
    expect(revoked).toEqual(["refresh-ana"]);
    expect(jar.get(ACCESS_COOKIE)).toBe("access-bia");
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-bia");
  });

  it("o link que não vale não mexe na sessão, nem com a troca confirmada", async () => {
    signedIn("access-ana", "refresh-ana");
    const revoked = identity({ "access-ana": ANA });
    expect(await openGuestAccess("vencido", true)).toEqual({ ok: false, reason: "invalid" });
    expect(jar.get(REFRESH_COOKIE)).toBe("refresh-ana");
    expect(revoked).toEqual([]);
  });
});
