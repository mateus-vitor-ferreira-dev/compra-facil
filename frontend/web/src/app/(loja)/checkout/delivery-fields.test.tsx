import { render, screen } from "@testing-library/react";
import { EMPTY_DELIVERY } from "@/lib/delivery";
import { DeliveryFields } from "./delivery-fields";

const props = {
  delivery: EMPTY_DELIVERY,
  errors: {},
  remembered: false,
  onChange: () => {},
  onFill: () => {},
  register: () => {},
};

describe("DeliveryFields", () => {
  it("na demonstração, avisa junto do CPF para não usar dados reais (#280)", () => {
    render(<DeliveryFields {...props} demo />);
    expect(screen.getByText("Não use os seus dados reais.")).toBeInTheDocument();
    // quem usa leitor de tela ouve o aviso no próprio campo
    expect(screen.getByLabelText("CPF")).toHaveAccessibleDescription(/Não use os seus dados reais/);
  });

  it("fora da demonstração, não mostra o aviso", () => {
    render(<DeliveryFields {...props} />);
    expect(screen.queryByText("Não use os seus dados reais.")).not.toBeInTheDocument();
  });
});
