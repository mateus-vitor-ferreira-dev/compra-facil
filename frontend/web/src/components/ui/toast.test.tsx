import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Toaster, useToast } from "./toast";

function Trigger({ tone, message }: { tone: "success" | "error"; message: string }) {
  const { show } = useToast();
  return (
    <button type="button" onClick={() => show(tone, message)}>
      disparar
    </button>
  );
}

describe("Toaster", () => {
  it("sucesso é anunciado sem tirar o foco (status numa região aria-live)", async () => {
    render(
      <Toaster>
        <Trigger tone="success" message="Produto adicionado ao carrinho" />
      </Toaster>,
    );

    const button = screen.getByRole("button", { name: "disparar" });
    await userEvent.click(button);

    const toast = screen.getByRole("status");
    expect(toast).toHaveTextContent("Produto adicionado ao carrinho");
    expect(toast.parentElement).toHaveAttribute("aria-live", "polite");
    expect(button).toHaveFocus();
  });

  it("erro usa role=alert e o aviso fecha pelo botão", async () => {
    render(
      <Toaster>
        <Trigger tone="error" message="Não deu para adicionar: o estoque acabou" />
      </Toaster>,
    );

    await userEvent.click(screen.getByRole("button", { name: "disparar" }));
    expect(screen.getByRole("alert")).toHaveTextContent("estoque acabou");

    await userEvent.click(screen.getByRole("button", { name: "Fechar aviso" }));
    expect(screen.queryByRole("alert")).toBeNull();
  });

  it("some sozinho depois de alguns segundos", () => {
    vi.useFakeTimers();
    render(
      <Toaster>
        <Trigger tone="success" message="Salvo" />
      </Toaster>,
    );

    act(() => screen.getByRole("button", { name: "disparar" }).click());
    expect(screen.getByRole("status")).toBeInTheDocument();
    act(() => vi.advanceTimersByTime(6000));
    expect(screen.queryByRole("status")).toBeNull();
    vi.useRealTimers();
  });
});
