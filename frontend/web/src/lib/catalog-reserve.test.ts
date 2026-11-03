import { reserved, type Result } from "./catalog-api";
import { forgetAll } from "./catalog-reserve";

const ok = <T>(data: T): Promise<Result<T>> => Promise.resolve({ ok: true, data });
const fail = (status: number): Promise<Result<never>> => Promise.resolve({ ok: false, error: { status } });

describe("reserva da vitrine", () => {
  beforeEach(() => forgetAll());

  it("com a API fora, usa a última cópia boa, marcada como guardada", async () => {
    await reserved("product:1", ok({ name: "TV" }));
    expect(await reserved("product:1", fail(503))).toEqual({ ok: true, data: { name: "TV" }, stale: true });
    expect(await reserved("product:1", fail(502))).toMatchObject({ ok: true, stale: true });
  });

  it("sem cópia, o erro passa como veio", async () => {
    expect(await reserved("product:2", fail(503))).toEqual({ ok: false, error: { status: 503 } });
  });

  it("o 429 e o 404 são respostas de verdade, e não usam a cópia", async () => {
    await reserved("product:3", ok({ name: "Fone" }));
    expect(await reserved("product:3", fail(429))).toEqual({ ok: false, error: { status: 429 } });
    expect(await reserved("product:3", fail(404))).toEqual({ ok: false, error: { status: 404 } });
  });

  it("cada chave tem a própria cópia", async () => {
    await reserved("products:a", ok(["a"]));
    expect(await reserved("products:b", fail(503))).toMatchObject({ ok: false });
  });
});
