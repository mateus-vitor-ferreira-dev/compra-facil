import "server-only";
import { api } from "@/lib/api/client";
import { call, type Result } from "@/lib/catalog-api";

// o estoque muda com cada pedido: 15 s de cache seguram a vitrine sem mentir por muito tempo
// (quem decide de verdade é a reserva do pedido)
const STOCK_TTL = 15;

/** Quanto dá para vender de cada produto (`available`), por id; até 100 ids por chamada. */
export function getAvailability(ids: string[]): Promise<Result<Record<string, number>>> {
  return call(async () => {
    if (ids.length === 0) return { data: {}, response: new Response() };
    const r = await api.inventory({ revalidate: STOCK_TTL, tags: ["inventory:stock"] }).GET("/v1/stock", {
      params: { query: { product_ids: ids.slice(0, 100) } },
    });
    return {
      data: r.data ? Object.fromEntries(r.data.data.map((s) => [s.product_id, s.available])) : undefined,
      response: r.response,
    };
  });
}
