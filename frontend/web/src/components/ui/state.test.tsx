import { render, screen } from "@testing-library/react";
import { StateMessage } from "./state";

describe("StateMessage", () => {
  it("vazio: título no nível pedido, sem alerta", () => {
    render(<StateMessage headingLevel={3} title="Seu carrinho está vazio" description="..." />);

    expect(screen.getByRole("heading", { level: 3, name: "Seu carrinho está vazio" })).toBeInTheDocument();
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("erro: anunciado como alerta", () => {
    render(<StateMessage tone="error" title="Não encontramos essa página" description="..." />);

    expect(screen.getByRole("alert")).toHaveTextContent("Não encontramos essa página");
  });
});
