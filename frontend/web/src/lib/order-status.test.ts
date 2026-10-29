import {
  isFinal,
  orderHeadline,
  orderNumber,
  orderStatus,
  orderSteps,
  type OrderProgress,
} from "./order-status";

describe("orderStatus", () => {
  it("traduz a situação e o motivo do cancelamento", () => {
    expect(orderStatus({ status: "pending" })).toMatchObject({ label: "Processando", tone: "neutral" });
    expect(orderStatus({ status: "confirmed" })).toMatchObject({ label: "Em preparação", tone: "neutral" });
    expect(orderStatus({ status: "shipped" })).toMatchObject({ label: "Enviado", tone: "neutral" });
    expect(orderStatus({ status: "delivered" })).toMatchObject({ label: "Entregue", tone: "success" });
    expect(orderStatus({ status: "cancelled", cancellation_reason: "out_of_stock" }).detail).toContain(
      "esgotou",
    );
    expect(orderStatus({ status: "cancelled", cancellation_reason: "payment_failed" }).detail).toContain(
      "recusado",
    );
    expect(orderStatus({ status: "cancelled", cancellation_reason: null }).detail).toContain(
      "Nada foi cobrado",
    );
  });

  it("fornecedor não atendeu: diz se o estorno está em andamento ou feito", () => {
    const cancelled = { status: "cancelled", cancellation_reason: "supplier_failed" } as const;
    expect(orderStatus(cancelled).detail).toContain("Estamos estornando");
    expect(orderStatus({ ...cancelled, refunded_at: "2026-10-28T10:00:00Z" }).detail).toContain(
      "foi estornado",
    );
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
const paid: OrderProgress = {
  ...base,
  status: "confirmed",
  stock_reserved_at: "2026-10-27T15:00:01Z",
  updated_at: "2026-10-27T15:00:02Z",
};

describe("orderSteps", () => {
  it("pendente sem reserva: reservando o estoque", () => {
    expect(states(base)).toEqual(["done", "current", "upcoming"]);
    expect(orderHeadline(base)).toBe("Reservando o estoque");
    expect(orderStatus(base).detail).toBe("Reservando o estoque.");
  });

  it("pendente com reserva: cobrando o pagamento, com o horário da reserva", () => {
    const o = { ...base, stock_reserved_at: "2026-10-27T15:00:01Z" };
    expect(states(o)).toEqual(["done", "done", "current"]);
    expect(orderSteps(o)[1]).toMatchObject({ label: "Estoque reservado", at: "2026-10-27T15:00:01Z" });
    expect(orderHeadline(o)).toBe("Cobrando o pagamento");
  });

  it("pago: indo ao fornecedor, e o envio e a entrega pela frente", () => {
    expect(states(paid)).toEqual(["done", "done", "done", "current", "upcoming", "upcoming"]);
    expect(orderSteps(paid)[3]?.label).toBe("Enviando ao fornecedor");
    expect(orderHeadline(paid)).toBe("Pagamento aprovado");

    const accepted = { ...paid, sent_to_supplier_at: "2026-10-27T15:00:05Z" };
    expect(orderSteps(accepted)[3]).toMatchObject({
      label: "Fornecedor separando",
      at: "2026-10-27T15:00:05Z",
    });
  });

  it("enviado e depois entregue, cada etapa no seu horário", () => {
    const shipped = {
      ...paid,
      status: "shipped" as const,
      sent_to_supplier_at: "2026-10-27T15:00:05Z",
      shipped_at: "2026-10-28T09:00:00Z",
    };
    expect(states(shipped)).toEqual(["done", "done", "done", "done", "current", "upcoming"]);
    expect(orderHeadline(shipped)).toBe("Pedido a caminho");

    const delivered = { ...shipped, status: "delivered" as const, delivered_at: "2026-10-30T14:00:00Z" };
    expect(states(delivered)).toEqual(["done", "done", "done", "done", "done", "done"]);
    expect(orderSteps(delivered)[5]?.at).toBe("2026-10-30T14:00:00Z");
    expect(orderHeadline(delivered)).toBe("Pedido entregue");
  });

  it("sem estoque: falha na reserva e o resto não acontece", () => {
    const o = { ...base, status: "cancelled" as const, cancellation_reason: "out_of_stock" as const };
    expect(states(o)).toEqual(["done", "failed", "skipped"]);
    expect(orderSteps(o)[1]?.label).toBe("Sem estoque");
  });

  it("pagamento recusado: o estoque foi reservado e a cobrança falhou", () => {
    const o = {
      ...base,
      status: "cancelled" as const,
      cancellation_reason: "payment_failed" as const,
      stock_reserved_at: "2026-10-27T15:00:01Z",
    };
    expect(states(o)).toEqual(["done", "done", "failed"]);
    expect(orderSteps(o)[2]?.label).toBe("Pagamento recusado");
  });

  it("fornecedor não atendeu: a entrega dá lugar ao estorno", () => {
    const o = { ...paid, status: "cancelled" as const, cancellation_reason: "supplier_failed" as const };
    expect(states(o)).toEqual(["done", "done", "done", "failed", "current"]);
    expect(orderSteps(o)[4]?.label).toBe("Estornando o valor pago");

    const refunded = { ...o, refunded_at: "2026-10-27T15:01:00Z" };
    expect(orderSteps(refunded)[4]).toMatchObject({ label: "Valor estornado", state: "done" });
  });

  it("pedido de antes da etapa existir (sem stock_reserved_at) ainda fecha certo", () => {
    expect(states({ ...paid, stock_reserved_at: undefined }).slice(0, 3)).toEqual(["done", "done", "done"]);
  });
});

describe("isFinal", () => {
  it("só o entregue e o cancelado sem estorno pendente não mudam mais", () => {
    expect(isFinal({ status: "pending" })).toBe(false);
    expect(isFinal({ status: "confirmed" })).toBe(false);
    expect(isFinal({ status: "shipped" })).toBe(false);
    expect(isFinal({ status: "delivered" })).toBe(true);
    expect(isFinal({ status: "cancelled", cancellation_reason: "out_of_stock" })).toBe(true);
    expect(isFinal({ status: "cancelled", cancellation_reason: "supplier_failed" })).toBe(false);
    expect(
      isFinal({
        status: "cancelled",
        cancellation_reason: "supplier_failed",
        refunded_at: "2026-10-28T10:00:00Z",
      }),
    ).toBe(true);
  });
});
