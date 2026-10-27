import { render, screen } from "@testing-library/react";
import { Button } from "./button";

describe("Button", () => {
  it("é type=button por padrão (não envia formulário sem querer)", () => {
    render(<Button>Ver detalhes</Button>);

    expect(screen.getByRole("button", { name: "Ver detalhes" })).toHaveAttribute("type", "button");
  });

  it("só ícone precisa de nome acessível", () => {
    render(
      <Button size="icon" aria-label="Buscar">
        <svg aria-hidden />
      </Button>,
    );

    expect(screen.getByRole("button", { name: "Buscar" })).toBeInTheDocument();
  });
});
