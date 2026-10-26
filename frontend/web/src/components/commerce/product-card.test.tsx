import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import type { Product } from "@/lib/catalog";
import { ProductCard } from "./product-card";

const product = {
  id: "0199f6a0-0000-7000-8000-000000000001",
  sku: "TV-1",
  name: 'Smart TV 55" QLED',
  description: "",
  category: { id: "c", slug: "tv", name: "TVs" },
  image_url: null,
  image: null,
  price: { amount_cents: 329990, currency: "BRL" },
  effective_price: { amount_cents: 263990, currency: "BRL" },
  campaign: { id: "k", name: "Black Friday", discount_percent: 20, ends_at: "2026-11-30T03:00:00Z" },
  created_at: "2026-10-01T10:00:00Z",
  updated_at: "2026-10-01T10:00:00Z",
} as Product;

describe("ProductCard", () => {
  it("um link só por produto, com o nome, para a página dele", async () => {
    render(<ProductCard product={product} />);

    const links = screen.getAllByRole("link");
    expect(links).toHaveLength(1);
    expect(links[0]).toHaveAccessibleName('Smart TV 55" QLED');
    expect(links[0]).toHaveAttribute("href", `/produto/${product.id}`);

    await userEvent.tab();
    expect(links[0]).toHaveFocus(); // um ponto de parada do teclado por card
  });

  it("mostra a campanha e o preço com desconto", () => {
    render(<ProductCard product={product} />);

    expect(screen.getByText("Black Friday")).toBeInTheDocument();
    expect(screen.getByText(/20% de desconto/)).toBeInTheDocument();
  });
});
