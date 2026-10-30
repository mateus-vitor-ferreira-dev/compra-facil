import { fireEvent, render, screen } from "@testing-library/react";
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
