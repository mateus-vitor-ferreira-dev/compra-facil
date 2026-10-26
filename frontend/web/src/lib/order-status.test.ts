import { orderNumber, orderStatus } from "./order-status";

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
