import { render, screen } from "@testing-library/react";
import { ProductImage } from "./product-image";

const image = {
  url: "http://cdn/tv-1.abc-683.webp",
  width: 683,
  height: 683,
  variants: [
    { width: 320, url: "http://cdn/tv-1.abc-320.webp" },
    { width: 683, url: "http://cdn/tv-1.abc-683.webp" },
  ],
};

describe("ProductImage", () => {
  it("monta o srcset com as variantes e reserva o espaço (width/height)", () => {
    render(<ProductImage image={image} alt="Smart TV" categorySlug="tv" sizes="50vw" />);

    const img = screen.getByRole("img", { name: "Smart TV" });
    expect(img).toHaveAttribute(
      "srcset",
      "http://cdn/tv-1.abc-320.webp 320w, http://cdn/tv-1.abc-683.webp 683w",
    );
    expect(img).toHaveAttribute("sizes", "50vw");
    expect(img).toHaveAttribute("src", "http://cdn/tv-1.abc-683.webp");
    expect(img).toHaveAttribute("width", "683");
    expect(img).toHaveAttribute("loading", "lazy");
  });

  it("a da primeira dobra carrega na hora, com prioridade", () => {
    render(<ProductImage image={image} alt="Smart TV" categorySlug="tv" sizes="50vw" priority />);

    const img = screen.getByRole("img", { name: "Smart TV" });
    expect(img).toHaveAttribute("loading", "eager");
    expect(img).toHaveAttribute("fetchpriority", "high");
  });

  it("foto só por URL (admin): sem srcset", () => {
    render(
      <ProductImage
        image={{ url: "http://outro/foto.jpg", width: null, height: null, variants: [] }}
        alt="Fone"
        categorySlug="fones"
        sizes="50vw"
      />,
    );

    const img = screen.getByRole("img", { name: "Fone" });
    expect(img).toHaveAttribute("src", "http://outro/foto.jpg");
    expect(img).not.toHaveAttribute("srcset");
  });

  it("sem foto: o ícone da categoria, anunciado como 'sem foto'", () => {
    render(<ProductImage image={null} alt="Notebook" categorySlug="notebooks" sizes="50vw" />);

    expect(screen.getByRole("img", { name: "Notebook (sem foto)" })).toBeInTheDocument();
  });
});
