import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { PixPayment } from "./pix-payment";

const START = new Date("2026-10-29T15:00:00Z");
const pix = {
  qr_code: "00020101021226870014br.gov.bcb.pix",
  qr_code_base64: "iVBORw0KGgo=",
  ticket_url: "http://localhost:8092/sim/pix/1",
  expires_at: "2026-10-29T15:30:00Z",
};

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true, now: START });
});
afterEach(() => vi.useRealTimers());

describe("PixPayment", () => {
  it("o valor, o tempo que falta, o copia e cola e o QR", async () => {
    render(<PixPayment pix={pix} amount="R$ 2.639,92" startedAt={START.toISOString()} />);
    expect(screen.getByRole("heading", { name: "Valor do PIX" })).toBeVisible();
    expect(screen.getByText("30:00")).toBeVisible();
    // o leitor de tela ouve a hora, não cada segundo
    expect(screen.getByText("Vence às 12:30")).toHaveClass("sr-only");
    expect(screen.getByRole("textbox", { name: "Código PIX copia e cola" })).toHaveValue(pix.qr_code);
    expect(screen.getByRole("img", { name: "QR code do PIX de R$ 2.639,92" })).toBeVisible();
    expect(screen.getByRole("link", { name: /Abrir a página do PIX/ })).toHaveAttribute(
      "href",
      pix.ticket_url,
    );

    await act(() => vi.advanceTimersByTimeAsync(61_000));
    expect(screen.getByText("28:59")).toBeVisible();
  });

  it("copia o código e avisa", async () => {
    const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
    const write = vi.spyOn(navigator.clipboard, "writeText").mockResolvedValue();
    render(<PixPayment pix={pix} amount="R$ 10,00" startedAt={START.toISOString()} />);
    await user.click(screen.getByRole("button", { name: "Copiar o código" }));
    // a cópia é assíncrona: o botão muda quando a área de transferência responde
    expect(await screen.findByRole("button", { name: "Código copiado" })).toBeVisible();
    expect(write).toHaveBeenCalledWith(pix.qr_code);
    expect(screen.getByText("Código copiado.")).toHaveClass("sr-only");
  });

  it("perto do fim avisa uma vez; vencido, some o código", async () => {
    render(<PixPayment pix={pix} amount="R$ 10,00" startedAt={START.toISOString()} />);
    await act(() => vi.advanceTimersByTimeAsync(26 * 60_000));
    expect(screen.getByText("Faltam menos de 5 minutos para o PIX vencer.")).toBeInTheDocument();

    await act(() => vi.advanceTimersByTimeAsync(5 * 60_000));
    expect(screen.getByRole("status")).toHaveTextContent("O PIX venceu.");
    expect(screen.queryByRole("button", { name: "Copiar o código" })).not.toBeInTheDocument();
    expect(screen.getByText("Venceu às")).toBeVisible();
  });
});
