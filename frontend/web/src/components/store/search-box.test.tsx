import { act, render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { SearchBox } from "./search-box";

const push = vi.fn();
vi.mock("next/navigation", () => ({
  useRouter: () => ({ push }),
  usePathname: () => "/",
  useSearchParams: () => new URLSearchParams(),
}));

const suggestions = [
  { kind: "category", slug: "notebooks", name: "Notebooks" },
  { kind: "product", id: "p1", name: "Notebook Dell Ryzen 5", category: "Notebooks" },
];

beforeEach(() => {
  vi.useFakeTimers({ shouldAdvanceTime: true });
  push.mockReset();
  vi.stubGlobal(
    "fetch",
    vi.fn(async () => new Response(JSON.stringify({ data: suggestions }), { status: 200 })),
  );
});

afterEach(() => {
  vi.useRealTimers();
  vi.unstubAllGlobals();
});

async function typeAndWait(text: string) {
  const user = userEvent.setup({ advanceTimers: vi.advanceTimersByTime });
  const input = screen.getByRole("combobox", { name: "Buscar produtos" });
  await user.type(input, text);
  await act(() => vi.advanceTimersByTimeAsync(250));
  return { user, input };
}

describe("SearchBox", () => {
  it("abaixo de 2 letras não pede sugestões", async () => {
    render(<SearchBox />);
    await typeAndWait("n");
    expect(fetch).not.toHaveBeenCalled();
    expect(screen.getByRole("combobox")).toHaveAttribute("aria-expanded", "false");
  });

  it("mostra as sugestões e anda por elas com as setas (aria-activedescendant)", async () => {
    render(<SearchBox />);
    const { user, input } = await typeAndWait("note");

    expect(fetch).toHaveBeenCalledTimes(1);
    expect(input).toHaveAttribute("aria-expanded", "true");
    const options = screen.getAllByRole("option");
    expect(options).toHaveLength(2);

    await user.keyboard("{ArrowDown}{ArrowDown}");
    expect(input).toHaveAttribute("aria-activedescendant", options[1]?.id);
    expect(options[1]).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Enter}");
    expect(push).toHaveBeenCalledWith("/produto/p1");
  });

  it("seta para cima a partir do campo vai para a última; Esc fecha a lista", async () => {
    render(<SearchBox />);
    const { user, input } = await typeAndWait("note");

    await user.keyboard("{ArrowUp}");
    expect(screen.getAllByRole("option")[1]).toHaveAttribute("aria-selected", "true");

    await user.keyboard("{Escape}");
    expect(input).toHaveAttribute("aria-expanded", "false");
    expect(input).not.toHaveAttribute("aria-activedescendant");
  });

  it("categoria sugerida leva para a página da categoria", async () => {
    render(<SearchBox />);
    const { user } = await typeAndWait("note");
    await user.keyboard("{ArrowDown}{Enter}");
    expect(push).toHaveBeenCalledWith("/categoria/notebooks");
  });

  it("sem sugestão marcada, o Enter fica com o formulário (busca o texto em /busca)", async () => {
    render(<SearchBox />);
    const { input } = await typeAndWait("note");
    expect(input.closest("form")).toHaveAttribute("action", "/busca");
    expect(input).toHaveAttribute("name", "q");
    expect(push).not.toHaveBeenCalled();
  });
});
