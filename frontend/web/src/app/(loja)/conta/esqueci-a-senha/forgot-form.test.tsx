import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { requestPasswordReset } from "../../password-actions";
import { ForgotForm } from "./forgot-form";

vi.mock("../../password-actions", () => ({ requestPasswordReset: vi.fn() }));

afterEach(() => vi.mocked(requestPasswordReset).mockReset());

describe("ForgotForm", () => {
  it("enviado, diz o mesmo exista a conta ou não, e o foco vai para o aviso", async () => {
    vi.mocked(requestPasswordReset).mockResolvedValue({ email: "ana@exemplo.com", sent: true });
    const user = userEvent.setup();
    render(<ForgotForm />);

    await user.type(screen.getByLabelText("E-mail"), "ana@exemplo.com");
    await user.click(screen.getByRole("button", { name: "Mandar o link" }));

    expect(await screen.findByRole("status")).toHaveTextContent(
      "Se houver uma conta com ana@exemplo.com, enviamos um link",
    );
    expect(screen.getByRole("heading", { name: "Confira seu e-mail" })).toHaveFocus();
    expect(Object.fromEntries(vi.mocked(requestPasswordReset).mock.calls[0]![1])).toEqual({
      email: "ana@exemplo.com",
    });
  });

  it("e-mail inválido fica no campo, com o motivo, e o foco volta a ele", async () => {
    vi.mocked(requestPasswordReset).mockResolvedValue({ email: "ana", field: "Confira o e-mail." });
    const user = userEvent.setup();
    render(<ForgotForm />);

    await user.type(screen.getByLabelText("E-mail"), "ana");
    await user.click(screen.getByRole("button", { name: "Mandar o link" }));

    const email = await screen.findByLabelText("E-mail");
    expect(email).toHaveAccessibleDescription("Confira o e-mail.");
    expect(email).toHaveFocus();
    expect(screen.queryByRole("status")).not.toBeInTheDocument();
  });

  it("a loja fora do ar vira alerta", async () => {
    vi.mocked(requestPasswordReset).mockResolvedValue({
      email: "ana@exemplo.com",
      error: "O link não foi enviado agora. Tente de novo em instantes.",
    });
    const user = userEvent.setup();
    render(<ForgotForm />);

    await user.type(screen.getByLabelText("E-mail"), "ana@exemplo.com");
    await user.click(screen.getByRole("button", { name: "Mandar o link" }));

    expect(await screen.findByRole("alert")).toHaveTextContent("O link não foi enviado agora");
  });
});
