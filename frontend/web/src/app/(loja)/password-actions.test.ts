import { signOut } from "@/lib/auth/flows";
import { requestPasswordReset, resetPassword } from "./password-actions";

vi.mock("@/lib/auth/flows", () => ({ signOut: vi.fn(async () => {}) }));

const ACCOUNT = "0199f6a0-0000-7000-8000-000000000a1a";

const form = (fields: Record<string, string>) => {
  const data = new FormData();
  for (const [k, v] of Object.entries(fields)) data.set(k, v);
  return data;
};

/** O identity falso: guarda as chamadas e responde com o que o teste mandou. */
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

const problem422 = (field: string) =>
  Response.json(
    { type: "about:blank", title: "Dados inválidos", status: 422, errors: [{ field, message: "x" }] },
    { status: 422, headers: { "Content-Type": "application/problem+json" } },
  );

afterEach(() => {
  vi.unstubAllGlobals();
  vi.mocked(signOut).mockClear();
});

describe("requestPasswordReset", () => {
  it("manda o e-mail ao identity e diz que enviou (o 202 vem exista a conta ou não)", async () => {
    const calls = identity(() => new Response(null, { status: 202 }));

    const state = await requestPasswordReset({}, form({ email: "  ana@exemplo.com " }));

    expect(state).toEqual({ email: "ana@exemplo.com", sent: true });
    expect(calls).toEqual([
      { url: "http://localhost/identity/v1/accounts/password-reset", body: { email: "ana@exemplo.com" } },
    ]);
  });

  it("e-mail inválido não chama o identity e aponta o campo", async () => {
    const calls = identity(() => new Response(null, { status: 202 }));

    const state = await requestPasswordReset({}, form({ email: "ana" }));

    expect(state.field).toMatch(/Confira o e-mail/);
    expect(state.sent).toBeUndefined();
    expect(calls).toHaveLength(0);
  });

  it("429 e falha da loja viram aviso, sem dizer que enviou", async () => {
    identity(() => new Response(null, { status: 429 }));
    expect((await requestPasswordReset({}, form({ email: "ana@exemplo.com" }))).error).toMatch(
      /Muitos pedidos/,
    );

    identity(() => new Response(null, { status: 503 }));
    expect(await requestPasswordReset({}, form({ email: "ana@exemplo.com" }))).toEqual({
      email: "ana@exemplo.com",
      error: "O link não foi enviado agora. Tente de novo em instantes.",
    });
  });
});

describe("resetPassword", () => {
  const valid = { conta: ACCOUNT, token: "tok", password: "senha-nova-1", confirm: "senha-nova-1" };

  it("troca a senha e tira este navegador da conta (o identity derrubou as sessões)", async () => {
    const calls = identity(() => new Response(null, { status: 204 }));

    const state = await resetPassword({}, form(valid));

    expect(state).toEqual({ status: "done" });
    expect(calls).toEqual([
      {
        url: "http://localhost/identity/v1/accounts/password-reset/confirm",
        body: { account_id: ACCOUNT, token: "tok", password: "senha-nova-1" },
      },
    ]);
    expect(signOut).toHaveBeenCalledOnce();
  });

  it("senha fora das regras ou diferente da repetição não chama o identity", async () => {
    const calls = identity(() => new Response(null, { status: 204 }));

    expect(await resetPassword({}, form({ ...valid, password: "curta", confirm: "curta" }))).toEqual({
      fields: { password: "A senha precisa de pelo menos 8 caracteres." },
    });
    expect(await resetPassword({}, form({ ...valid, confirm: "outra-senha" }))).toEqual({
      fields: { confirm: "As duas senhas não são iguais." },
    });
    expect(calls).toHaveLength(0);
  });

  it("link sem conta ou sem token é inválido, sem chamar o identity", async () => {
    const calls = identity(() => new Response(null, { status: 204 }));

    expect(await resetPassword({}, form({ ...valid, conta: "nao-e-uuid" }))).toEqual({ status: "invalid" });
    expect(await resetPassword({}, form({ ...valid, token: "" }))).toEqual({ status: "invalid" });
    expect(calls).toHaveLength(0);
  });

  it("422 no token é link vencido ou usado; 422 na senha aponta o campo", async () => {
    identity(() => problem422("token"));
    expect(await resetPassword({}, form(valid))).toEqual({ status: "invalid" });

    identity(() => problem422("password"));
    expect((await resetPassword({}, form(valid))).fields?.password).toMatch(/Escolha outra senha/);
    expect(signOut).not.toHaveBeenCalled();
  });

  it("falha da loja vira aviso de tentar de novo", async () => {
    identity(() => new Response(null, { status: 503 }));

    const state = await resetPassword({}, form(valid));

    expect(state.status).toBe("error");
    expect(state.error).toMatch(/A senha não foi trocada/);
  });
});
