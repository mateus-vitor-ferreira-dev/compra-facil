import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { resetPassword } from "../../password-actions";
import { ResetForm } from "./reset-form";

vi.mock("../../password-actions", () => ({ resetPassword: vi.fn(async () => ({ status: "done" })) }));

const ACCOUNT = "0199f6a0-0000-7000-8000-000000000a1a";

afterEach(() => vi.mocked(resetPassword).mockClear());

describe("ResetForm", () => {
  it("sem conta ou token no link, oferece pedir outro", () => {
    render(<ResetForm accountId="" token="" />);

    expect(screen.getByRole("alert")).toHaveTextContent("Este link venceu ou já foi usado");
    expect(screen.getByRole("link", { name: "Pedir outro link" })).toHaveAttribute(
      "href",
      "/conta/esqueci-a-senha",
    );
    expect(screen.queryByLabelText("Senha nova")).not.toBeInTheDocument();
  });

  it("senha curta é avisada no navegador, sem enviar, e o foco vai para ela", async () => {
    const user = userEvent.setup();
    render(<ResetForm accountId={ACCOUNT} token="tok" />);

    await user.type(screen.getByLabelText("Senha nova"), "curta");
    await user.click(screen.getByRole("button", { name: "Trocar a senha" }));

    expect(screen.getByText("A senha precisa de pelo menos 8 caracteres.")).toBeInTheDocument();
    expect(screen.getByLabelText("Senha nova")).toHaveFocus();
    expect(screen.getByLabelText("Senha nova")).toHaveAccessibleDescription(/pelo menos 8 caracteres/);
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it("as duas senhas diferentes são avisadas na repetição", async () => {
    const user = userEvent.setup();
    render(<ResetForm accountId={ACCOUNT} token="tok" />);

    await user.type(screen.getByLabelText("Senha nova"), "senha-nova-1");
    await user.type(screen.getByLabelText("Repita a senha nova"), "senha-nova-2");
    await user.click(screen.getByRole("button", { name: "Trocar a senha" }));

    expect(screen.getByText("As duas senhas não são iguais.")).toBeInTheDocument();
    expect(screen.getByLabelText("Repita a senha nova")).toHaveFocus();
    expect(resetPassword).not.toHaveBeenCalled();
  });

  it("senha válida vai para a server action com a conta e o token; trocada, o foco vai para Entrar", async () => {
    const user = userEvent.setup();
    render(<ResetForm accountId={ACCOUNT} token="tok" />);

    await user.type(screen.getByLabelText("Senha nova"), "senha-nova-1");
    await user.type(screen.getByLabelText("Repita a senha nova"), "senha-nova-1");
    await user.click(screen.getByRole("button", { name: "Trocar a senha" }));

    expect(await screen.findByRole("status")).toHaveTextContent("Senha trocada");
    expect(screen.getByRole("link", { name: "Entrar" })).toHaveFocus();
    const sent = vi.mocked(resetPassword).mock.calls[0]![1];
    expect(Object.fromEntries(sent)).toEqual({
      conta: ACCOUNT,
      token: "tok",
      password: "senha-nova-1",
      confirm: "senha-nova-1",
    });
  });

  it("mostrar a senha troca o tipo do campo", async () => {
    const user = userEvent.setup();
    render(<ResetForm accountId={ACCOUNT} token="tok" />);

    await user.click(screen.getAllByRole("button", { name: "Mostrar a senha" })[0]!);

    expect(screen.getByLabelText("Senha nova")).toHaveAttribute("type", "text");
  });
});
