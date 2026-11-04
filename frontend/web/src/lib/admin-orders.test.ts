import { describe, expect, it } from "vitest";
import { adminOrderStatus, apiFilter, filterSearch, ordersQuery, whatsappHref } from "./admin-orders";

describe("pedidos do painel", () => {
  it("o cancelado diz o porquê no selo", () => {
    expect(adminOrderStatus({ status: "cancelled", cancellation_reason: "supplier_failed" })).toEqual({
      label: "Cancelado pelo fornecedor",
      tone: "danger",
    });
    expect(adminOrderStatus({ status: "delivered" }).tone).toBe("success");
    expect(adminOrderStatus({ status: "pending" }).label).toBe("Aguardando pagamento");
  });

  it("lê o filtro da URL e ignora o que não vale", () => {
    const q = ordersQuery({ q: "  ana@x.com ", situacao: "roxo", de: "2026-11-01", ate: "2026-11-04" });
    expect(q.q).toBe("ana@x.com");
    expect(q.status).toBeUndefined();
    expect(apiFilter(q)).toEqual({
      q: "ana@x.com",
      status: undefined,
      from: "2026-11-01T03:00:00.000Z",
      to: "2026-11-05T03:00:00.000Z",
      cursor: undefined,
    });
  });

  it("período inválido: a API recebe sem período", () => {
    const q = ordersQuery({ de: "2026-11-09", ate: "2026-11-01" });
    expect(q.period.error).toBeTruthy();
    expect(apiFilter(q).from).toBeUndefined();
  });

  it("os links mantêm o filtro", () => {
    const q = ordersQuery({ q: "bia", situacao: "shipped", de: "2026-11-01" });
    expect(filterSearch(q)).toBe("?q=bia&situacao=shipped&de=2026-11-01");
    expect(filterSearch(ordersQuery({}), { depois: "abc" })).toBe("?depois=abc");
    expect(filterSearch(ordersQuery({}))).toBe("");
  });

  it("WhatsApp só com DDD e número", () => {
    expect(whatsappHref("35999990000")).toBe("https://wa.me/5535999990000");
    expect(whatsappHref("(35) 3821-0000")).toBe("https://wa.me/553538210000");
    expect(whatsappHref("123")).toBeNull();
    expect(whatsappHref(null)).toBeNull();
  });
});
