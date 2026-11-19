import { render, screen } from "@testing-library/react";
import { MfaForm } from "./mfa-form";

vi.mock("./actions", () => ({ verifyMfa: vi.fn(async () => ({})) }));

beforeEach(() => vi.stubGlobal("matchMedia", () => ({ matches: false })));
afterEach(() => vi.unstubAllGlobals());

describe("MfaForm", () => {
  it("o código do app: rótulo, teclado numérico e autocomplete de código único", () => {
    render(<MfaForm recovery={false} />);

    const field = screen.getByLabelText("Código do app autenticador");
    expect(field).toHaveAttribute("autocomplete", "one-time-code");
    expect(field).toHaveAttribute("inputmode", "numeric");
    expect(field).toHaveAttribute("name", "code");
    expect(field).toHaveAccessibleDescription(/6 números/);
    expect(screen.getByRole("button", { name: "Confirmar" })).toBeInTheDocument();
  });

  it("o código de recuperação aceita letras e não sugere código do SMS", () => {
    render(<MfaForm recovery />);

    const field = screen.getByLabelText("Código de recuperação");
    expect(field).toHaveAttribute("name", "recovery_code");
    expect(field).toHaveAttribute("autocomplete", "off");
    expect(field).not.toHaveAttribute("inputmode");
    expect(screen.queryByLabelText("Código do app autenticador")).not.toBeInTheDocument();
  });
});
