import { render, screen } from "@testing-library/react";
import { CompanyData } from "./company-data";

describe("CompanyData", () => {
  it("sem dados configurados, não mostra nada", () => {
    const { container } = render(<CompanyData />);
    expect(container).toBeEmptyDOMElement();
  });

  it("mostra só o que foi configurado", () => {
    render(<CompanyData cnpj="00.000.000/0001-00" email="contato@exemplo.com" />);
    expect(screen.getByText("CNPJ 00.000.000/0001-00")).toBeInTheDocument();
    expect(screen.getByRole("link", { name: "contato@exemplo.com" })).toHaveAttribute(
      "href",
      "mailto:contato@exemplo.com",
    );
    expect(screen.queryByText(/undefined/)).not.toBeInTheDocument();
  });
});
