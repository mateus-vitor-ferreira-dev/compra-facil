import { render, screen } from "@testing-library/react";
import { Price } from "./price";

describe("Price", () => {
  it("sem campanha, mostra só o preço", () => {
    const { container } = render(
      <Price price={{ amount_cents: 19990 }} effective={{ amount_cents: 19990 }} />,
    );

    expect(screen.getByText(/199,90/)).toBeInTheDocument();
    expect(container.querySelector("s")).toBeNull();
  });

  it("com campanha, risca o de tabela e lê 'de ... por ...' no leitor de tela", () => {
    const { container } = render(
      <Price
        price={{ amount_cents: 19990, currency: "BRL" }}
        effective={{ amount_cents: 13993, currency: "BRL" }}
      />,
    );

    expect(container.querySelector("s")).toHaveTextContent(/de\s*R\$\s*199,90/);
    expect(container).toHaveTextContent(/por\s*R\$\s*139,93/);
  });
});
