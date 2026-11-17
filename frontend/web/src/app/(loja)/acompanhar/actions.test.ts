import { redirect } from "next/navigation";
import { openGuestAccess } from "@/lib/auth/flows";
import { openGuestLink } from "./actions";

vi.mock("@/lib/auth/flows", () => ({ openGuestAccess: vi.fn(), resendGuestAccess: vi.fn() }));
vi.mock("next/navigation", () => ({
  redirect: vi.fn((to: string) => {
    throw new Error(`redirect ${to}`);
  }),
}));

const form = (fields: Record<string, string>) => {
  const data = new FormData();
  for (const [k, v] of Object.entries(fields)) data.set(k, v);
  return data;
};

/** Para onde a ação mandou o navegador. */
async function target(fields: Record<string, string>): Promise<string> {
  await expect(openGuestLink(form(fields))).rejects.toThrow(/^redirect /);
  return vi.mocked(redirect).mock.calls.at(-1)![0];
}

afterEach(() => vi.clearAllMocks());

describe("openGuestLink", () => {
  it("abre a sessão e vai para a conta", async () => {
    vi.mocked(openGuestAccess).mockResolvedValue({ ok: true });
    expect(await target({ token: "link-bom" })).toBe("/conta");
    expect(openGuestAccess).toHaveBeenCalledWith("link-bom", false);
  });

  it("com a conta aberta, volta ao link para o comprador sair antes", async () => {
    vi.mocked(openGuestAccess).mockResolvedValue({
      ok: false,
      reason: "other",
      current: "account",
      email: "co***@exemplo.com",
    });
    const to = new URL(await target({ token: "link-bom" }), "http://loja");
    expect(to.pathname).toBe("/acompanhar/abrir");
    expect(Object.fromEntries(to.searchParams)).toEqual({
      token: "link-bom",
      outra: "conta",
      email: "co***@exemplo.com",
    });
  });

  it("com outra compra sem conta aberta, volta para confirmar; confirmado, pede a troca", async () => {
    vi.mocked(openGuestAccess).mockResolvedValue({
      ok: false,
      reason: "other",
      current: "guest",
      email: "co***@exemplo.com",
    });
    expect(await target({ token: "link-bom" })).toContain("outra=convidado");
    vi.mocked(openGuestAccess).mockResolvedValue({ ok: true });
    expect(await target({ token: "link-bom", trocar: "1" })).toBe("/conta");
    expect(openGuestAccess).toHaveBeenLastCalledWith("link-bom", true);
  });

  it("o link que não vale volta para pedir outro", async () => {
    vi.mocked(openGuestAccess).mockResolvedValue({ ok: false, reason: "invalid" });
    expect(await target({ token: "vencido" })).toBe("/acompanhar?erro=link");
    expect(await target({ token: "" })).toBe("/acompanhar?erro=link");
  });
});
