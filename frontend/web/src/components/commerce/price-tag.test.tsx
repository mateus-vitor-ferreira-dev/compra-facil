import { render, screen } from "@testing-library/react";
import { PriceTag } from "./price-tag";

const brl = (amount_cents: number) => ({ amount_cents, currency: "BRL" });
const normalize = (s: string | null) => (s ?? "").replace(/\s/g, " ");

describe("PriceTag", () => {
  it("sem campanha: o preço, sem riscado nem desconto", () => {
    const { container } = render(<PriceTag price={brl(49990)} effective={brl(49990)} />);

    expect(normalize(screen.getByText(/499,90/).textContent)).toBe("R$ 499,90");
    expect(container.querySelector("s")).toBeNull();
    expect(container).not.toHaveTextContent("%");
  });

  it("com campanha: o leitor de tela ouve de/por e o desconto numa frase só", () => {
    render(<PriceTag price={brl(329990)} effective={brl(263990)} />);

    const phrase = normalize(screen.getByText(/de R\$/, { selector: "p.sr-only" }).textContent);
    expect(phrase).toBe("de R$ 3.299,90 por R$ 2.639,90, 20% de desconto");
  });

  it("a parte visual (centavos em cima, etiqueta) fica fora da árvore de acessibilidade", () => {
    const { container } = render(<PriceTag price={brl(329990)} effective={brl(263990)} />);

    const visual = [...container.querySelectorAll('[aria-hidden="true"]')]
      .map((e) => e.textContent)
      .join(" ");
    expect(visual).toContain("2.639");
    expect(visual).toContain("90");
    expect(visual).toContain("-20%");
  });
});
