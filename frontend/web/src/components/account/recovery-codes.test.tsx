import { render, screen, within } from "@testing-library/react";
import { RecoveryCodes } from "./recovery-codes";

const codes = Array.from({ length: 10 }, (_, i) => `abcd${i}-efgh${i}`);

describe("RecoveryCodes", () => {
  it("lista os dez códigos, avisa que não aparecem de novo e oferece copiar e baixar", () => {
    render(<RecoveryCodes codes={codes} />);

    const list = screen.getByRole("list");
    expect(
      within(list)
        .getAllByRole("listitem")
        .map((li) => li.textContent),
    ).toEqual(codes);
    expect(screen.getByText(/não aparecem de novo/)).toBeInTheDocument();
    expect(screen.getByRole("button", { name: "Copiar os códigos" })).toBeInTheDocument();
    const download = screen.getByRole("link", { name: "Baixar em .txt" });
    expect(download).toHaveAttribute("download", "embrulhado-codigos-de-recuperacao.txt");
    expect(decodeURIComponent(download.getAttribute("href")!)).toContain(codes.join("\n"));
  });
});
