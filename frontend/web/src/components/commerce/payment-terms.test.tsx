import { render, screen } from "@testing-library/react";
import { PaymentTerms } from "./payment-terms";

const brl = (amount_cents: number) => ({ amount_cents, currency: "BRL" });
// o formatador separa "R$" do valor com espaço não separável
const money = (s: string) => new RegExp(s.replace("R$ ", "R\\$\\s"));

describe("PaymentTerms", () => {
  it("mostra as 12 parcelas sem juros quando o preço permite", () => {
    render(<PaymentTerms price={brl(122003)} size="lg" />);
    expect(screen.getByText(/à vista no PIX ou em até 12x de/)).toHaveTextContent(
      money("R$ 101,67 sem juros"),
    );
  });

  it("não oferece parcela menor que R$ 5,00", () => {
    render(<PaymentTerms price={brl(2000)} />);
    expect(screen.getByText(/no PIX ou 4x de/)).toHaveTextContent(money("R$ 5,00 sem juros"));
  });

  it("sem parcelas possíveis, não promete parcelamento", () => {
    render(<PaymentTerms price={brl(900)} />);
    expect(screen.getByText("à vista no PIX ou no cartão")).toBeInTheDocument();
  });
});
