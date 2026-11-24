import { fireEvent, render, screen } from "@testing-library/react";
import { act } from "react";
import { announceColor, onColor } from "@/lib/product-color-sync";
import { ProductGallery } from "./product-gallery";

const photo = (name: string) => ({
  url: `http://cdn/${name}-683.webp`,
  width: 683,
  height: 683,
  variants: [
    { width: 320, url: `http://cdn/${name}-320.webp` },
    { width: 683, url: `http://cdn/${name}-683.webp` },
  ],
});
const images = [photo("a"), photo("b"), photo("c")];
const gallery = () => <ProductGallery images={images} alt="Smart TV" categorySlug="tv" sizes="50vw" />;

describe("ProductGallery", () => {
  beforeEach(() => {
    Element.prototype.scrollTo = vi.fn();
  });

  it("uma foto só não tem miniaturas", () => {
    render(<ProductGallery images={[photo("a")]} alt="Smart TV" categorySlug="tv" sizes="50vw" />);
    expect(screen.getByRole("img", { name: "Smart TV" })).toHaveAttribute("loading", "eager");
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("sem foto, mostra o ícone da categoria", () => {
    render(<ProductGallery images={[]} alt="Smart TV" categorySlug="tv" sizes="50vw" />);
    expect(screen.getByRole("img", { name: "Smart TV (sem foto)" })).toBeInTheDocument();
  });

  it("cada foto tem o próprio srcset, e só a primeira carrega na hora", () => {
    render(gallery());
    expect(screen.getByRole("img", { name: "Smart TV" })).toHaveAttribute("loading", "eager");
    const second = screen.getByRole("img", { name: "Smart TV, foto 2 de 3" });
    expect(second).toHaveAttribute("loading", "lazy");
    expect(second).toHaveAttribute("srcset", expect.stringContaining("b-320.webp 320w"));
  });

  it("a miniatura leva à foto e fica marcada", () => {
    render(gallery());
    const third = screen.getByRole("button", { name: "Ver a foto 3 de 3" });
    fireEvent.click(third);
    expect(third).toHaveAttribute("aria-current", "true");
    expect(screen.getByRole("button", { name: "Ver a foto 1 de 3" })).not.toHaveAttribute("aria-current");
    expect(screen.getByText("Foto 3 de 3")).toBeInTheDocument();
  });

  it("as setas andam pelas fotos, sem passar das pontas, e o foco acompanha nas miniaturas", () => {
    render(gallery());
    const first = screen.getByRole("button", { name: "Ver a foto 1 de 3" });
    first.focus();
    fireEvent.keyDown(first, { key: "ArrowLeft" });
    expect(first).toHaveAttribute("aria-current", "true");
    fireEvent.keyDown(first, { key: "ArrowRight" });
    expect(screen.getByRole("button", { name: "Ver a foto 2 de 3" })).toHaveFocus();
    fireEvent.keyDown(document.activeElement!, { key: "End" });
    expect(screen.getByRole("button", { name: "Ver a foto 3 de 3" })).toHaveFocus();
  });
});

describe("ProductGallery com cores", () => {
  beforeEach(() => {
    Element.prototype.scrollTo = vi.fn();
  });

  const colored = (colors?: string[], photos = images) => (
    <ProductGallery
      images={photos}
      alt="Camiseta"
      categorySlug="gamer"
      sizes="50vw"
      productId="p1"
      colors={colors}
    />
  );

  it("a cor escolhida no formulário traz a foto dela", () => {
    render(colored(["Branco", "Preto", "Vermelho"]));
    act(() => announceColor("p1", "Preto", "seletor"));
    expect(screen.getByRole("button", { name: "Ver a foto 2 de 3, cor Preto" })).toHaveAttribute(
      "aria-current",
      "true",
    );
  });

  it("a cor de outro produto não mexe na foto", () => {
    render(colored(["Branco", "Preto", "Vermelho"]));
    act(() => announceColor("outro", "Preto", "seletor"));
    expect(screen.getByRole("button", { name: "Ver a foto 1 de 3, cor Branco" })).toHaveAttribute(
      "aria-current",
      "true",
    );
  });

  it("a miniatura de uma cor marca a cor no formulário", () => {
    const visto = vi.fn();
    const parar = onColor("p1", "galeria", visto);
    render(colored(["Branco", "Preto", "Vermelho"]));
    fireEvent.click(screen.getByRole("button", { name: "Ver a foto 3 de 3, cor Vermelho" }));
    expect(visto).toHaveBeenCalledWith("Vermelho");
    parar();
  });

  it("com menos fotos que cores, não há ligação", () => {
    const visto = vi.fn();
    const parar = onColor("p1", "galeria", visto);
    render(colored(["Branco", "Preto", "Vermelho", "Azul"]));
    fireEvent.click(screen.getByRole("button", { name: "Ver a foto 2 de 3" }));
    act(() => announceColor("p1", "Preto", "seletor"));
    expect(visto).not.toHaveBeenCalled();
    expect(screen.getByRole("button", { name: "Ver a foto 2 de 3" })).toHaveAttribute("aria-current", "true");
    parar();
  });
});
