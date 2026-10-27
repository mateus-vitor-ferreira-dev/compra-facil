import { orderHeadline, orderNumber, orderStatus, orderSteps, type OrderProgress } from "./order-status";

describe("orderStatus", () => {
  it("traduz a situação e o motivo do cancelamento", () => {
    expect(orderStatus("pending")).toMatchObject({ label: "Processando", tone: "neutral" });
    expect(orderStatus("confirmed")).toMatchObject({ label: "Confirmado", tone: "success" });
    expect(orderStatus("cancelled", "out_of_stock").detail).toContain("esgotou");
    expect(orderStatus("cancelled", "payment_failed").detail).toContain("recusado");
    expect(orderStatus("cancelled", null).detail).toContain("Nada foi cobrado");
  });

  it("orderNumber mostra os 8 primeiros caracteres em maiúsculas", () => {
    expect(orderNumber("4f2a9c1b-0000-7000-8000-000000000001")).toBe("4F2A9C1B");
  });
});

const base: OrderProgress = {
  status: "pending",
  cancellation_reason: null,
  stock_reserved_at: null,
  created_at: "2026-10-27T15:00:00Z",
  updated_at: "2026-10-27T15:00:00Z",
};
const states = (o: OrderProgress) => orderSteps(o).map((s) => s.state);

describe("orderSteps", () => {
  it("pendente sem reserva: reservando o estoque", () => {
    expect(states(base)).toEqual(["done", "current", "upcoming", "upcoming"]);
    expect(orderHeadline(base)).toBe("Reservando o estoque");
    expect(orderStatus("pending", null, null).detail).toBe("Reservando o estoque.");
  });

  it("pendente com reserva: cobrando o pagamento, com o horário da reserva", () => {
    const o = { ...base, stock_reserved_at: "2026-10-27T15:00:01Z" };
    expect(states(o)).toEqual(["done", "done", "current", "upcoming"]);
    expect(orderSteps(o)[1]).toMatchObject({ label: "Estoque reservado", at: "2026-10-27T15:00:01Z" });
    expect(orderHeadline(o)).toBe("Cobrando o pagamento");
  });

  it("confirmado: tudo feito, a confirmação no horário da última mudança", () => {
    const o = {
      ...base,
      status: "confirmed" as const,
      stock_reserved_at: "2026-10-27T15:00:01Z",
      updated_at: "2026-10-27T15:00:02Z",
    };
    expect(states(o)).toEqual(["done", "done", "done", "done"]);
    expect(orderSteps(o)[3]?.at).toBe("2026-10-27T15:00:02Z");
    expect(orderHeadline(o)).toBe("Pedido confirmado");
  });

  it("sem estoque: falha na reserva e o resto não acontece", () => {
    const o = { ...base, status: "cancelled" as const, cancellation_reason: "out_of_stock" as const };
    expect(states(o)).toEqual(["done", "failed", "skipped", "skipped"]);
    expect(orderSteps(o)[1]?.label).toBe("Sem estoque");
  });

  it("pagamento recusado: o estoque foi reservado e a cobrança falhou", () => {
    const o = {
      ...base,
      status: "cancelled" as const,
      cancellation_reason: "payment_failed" as const,
      stock_reserved_at: "2026-10-27T15:00:01Z",
    };
    expect(states(o)).toEqual(["done", "done", "failed", "skipped"]);
    expect(orderSteps(o)[2]?.label).toBe("Pagamento recusado");
  });

  it("pedido de antes da etapa existir (sem stock_reserved_at) ainda fecha certo", () => {
    expect(states({ ...base, status: "confirmed", stock_reserved_at: undefined })).toEqual([
      "done",
      "done",
      "done",
      "done",
    ]);
  });
});
