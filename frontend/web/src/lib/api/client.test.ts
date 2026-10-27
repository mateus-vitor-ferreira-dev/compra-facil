import { api } from "./client";

describe("cliente das APIs", () => {
  afterEach(() => vi.unstubAllGlobals());

  it("manda as listas da query separadas por vírgula, como os contratos pedem", async () => {
    const urls: string[] = [];
    vi.stubGlobal(
      "fetch",
      vi.fn(async (request: Request) => {
        urls.push(request.url);
        return Response.json({ data: [] });
      }),
    );

    await api.inventory().GET("/v1/stock", { params: { query: { product_ids: ["a", "b"] } } });

    expect(decodeURIComponent(urls[0] ?? "")).toMatch(/\/inventory\/v1\/stock\?product_ids=a,b$/);
  });
});
