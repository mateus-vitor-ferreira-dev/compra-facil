import type { Metadata } from "next";
import type { ReactNode } from "react";
import { Search, ShoppingCart, User } from "lucide-react";
import {
  EmptyCartIllustration,
  NotFoundIllustration,
  OrderConfirmedIllustration,
} from "@/components/brand/illustrations";
import { Logo } from "@/components/brand/logo";
import { CategoryIcon } from "@/components/commerce/category-icon";
import { PaymentTerms } from "@/components/commerce/payment-terms";
import { PriceTag } from "@/components/commerce/price-tag";
import { ProductCard, ProductCardSkeleton } from "@/components/commerce/product-card";
import { Badge } from "@/components/ui/badge";
import { Button } from "@/components/ui/button";
import { StateMessage } from "@/components/ui/state";
import type { Product } from "@/lib/catalog";
import { ThemeToggle } from "./theme-toggle";
import { ToastDemo } from "./toast-demo";

export const metadata: Metadata = { title: "Design system", robots: { index: false } };

const media = "http://localhost/media/produtos";

function product(over: Partial<Product> & Pick<Product, "name" | "price" | "effective_price">): Product {
  return {
    id: crypto.randomUUID(),
    sku: "SKU-1",
    description: "",
    category: { id: crypto.randomUUID(), slug: "tv", name: "Televisores" },
    image_url: null,
    image: null,
    campaign: null,
    created_at: "2026-10-01T10:00:00Z",
    updated_at: "2026-10-01T10:00:00Z",
    ...over,
  } as Product;
}

const samples: Product[] = [
  product({
    name: 'Smart TV Samsung 55" QLED',
    price: { amount_cents: 329990, currency: "BRL" },
    effective_price: { amount_cents: 263990, currency: "BRL" },
    campaign: {
      id: crypto.randomUUID(),
      name: "Black Friday",
      discount_percent: 20,
      ends_at: "2026-11-30T03:00:00Z",
    },
    image: {
      url: `${media}/tv-1.d226c7dd-683.webp`,
      width: 683,
      height: 683,
      variants: [
        { width: 320, url: `${media}/tv-1.d226c7dd-320.webp` },
        { width: 480, url: `${media}/tv-1.d226c7dd-480.webp` },
        { width: 683, url: `${media}/tv-1.d226c7dd-683.webp` },
      ],
    },
  }),
  product({
    name: "Fone Bluetooth JBL sem fio com cancelamento de ruído",
    category: { id: crypto.randomUUID(), slug: "fones", name: "Fones de ouvido" },
    price: { amount_cents: 49990, currency: "BRL" },
    effective_price: { amount_cents: 49990, currency: "BRL" },
  }),
  product({
    name: "Notebook Lenovo Core i7 16GB SSD 512GB",
    category: { id: crypto.randomUUID(), slug: "notebooks", name: "Notebooks" },
    price: { amount_cents: 549990, currency: "BRL" },
    effective_price: { amount_cents: 494990, currency: "BRL" },
    campaign: {
      id: crypto.randomUUID(),
      name: "Semana do Notebook",
      discount_percent: 10,
      ends_at: "2026-11-02T03:00:00Z",
    },
  }),
];

const categoryNames: Record<string, string> = {
  tv: "Televisores",
  fones: "Fones",
  "caixas-de-som": "Caixas de som",
  notebooks: "Notebooks",
  monitores: "Monitores",
  perifericos: "Periféricos",
  geladeiras: "Geladeiras",
  fogoes: "Fogões",
  lavadoras: "Lavadoras",
  sofas: "Sofás",
  colchoes: "Colchões",
  bicicletas: "Bicicletas",
  fitness: "Fitness",
};

const categories = [
  "tv",
  "fones",
  "caixas-de-som",
  "notebooks",
  "monitores",
  "perifericos",
  "geladeiras",
  "fogoes",
  "lavadoras",
  "sofas",
  "colchoes",
  "bicicletas",
  "fitness",
];

const palette = [
  ["Papel", "bg-paper", "fundo"],
  ["Superfície", "bg-surface", "campos e painéis"],
  ["Tinta", "bg-ink", "texto e ações"],
  ["Tinta suave", "bg-ink-soft", "texto secundário"],
  ["Etiqueta", "bg-tag", "só preço de campanha"],
  ["Sucesso", "bg-success", "pedido confirmado"],
  ["Erro", "bg-danger", "falha e cancelamento"],
] as const;

function Section({ title, children }: { title: string; children: ReactNode }) {
  return (
    <section className="space-y-5 border-t pt-8">
      <h2 className="text-xl font-semibold tracking-tight">{title}</h2>
      {children}
    </section>
  );
}

export default function DesignSystem() {
  return (
    <main id="conteudo" className="mx-auto max-w-5xl space-y-12 px-4 py-10 sm:px-6">
      <header className="flex flex-wrap items-center justify-between gap-4">
        <Logo />
        <ThemeToggle />
      </header>

      <div className="max-w-2xl space-y-3">
        <h1 className="text-3xl font-bold tracking-tight sm:text-4xl">Design system</h1>
        <p className="text-ink-soft text-lg">
          Tinta e papel no dia a dia. O amarelo aparece só onde há desconto, como a etiqueta no encarte de
          loja.
        </p>
      </div>

      <Section title="Preço">
        <div className="flex flex-wrap items-end gap-10">
          <div className="space-y-2">
            <PriceTag
              size="lg"
              price={{ amount_cents: 329990, currency: "BRL" }}
              effective={{ amount_cents: 263990, currency: "BRL" }}
            />
            <PaymentTerms size="lg" price={{ amount_cents: 263990, currency: "BRL" }} />
          </div>
          <div className="space-y-1">
            <PriceTag
              price={{ amount_cents: 549990, currency: "BRL" }}
              effective={{ amount_cents: 494990, currency: "BRL" }}
            />
            <PaymentTerms price={{ amount_cents: 494990, currency: "BRL" }} />
          </div>
          <div className="space-y-1">
            <PriceTag
              price={{ amount_cents: 49990, currency: "BRL" }}
              effective={{ amount_cents: 49990, currency: "BRL" }}
            />
            <PaymentTerms price={{ amount_cents: 49990, currency: "BRL" }} />
          </div>
        </div>
      </Section>

      <Section title="Produtos">
        <ul className="grid grid-cols-2 gap-x-4 gap-y-8 sm:grid-cols-3 lg:grid-cols-4">
          {samples.map((p, i) => (
            <li key={p.id}>
              <ProductCard product={p} priority={i === 0} />
            </li>
          ))}
          <li>
            <ProductCardSkeleton />
          </li>
        </ul>
      </Section>

      <Section title="Ações">
        <div className="flex flex-wrap gap-3">
          <Button>
            <ShoppingCart aria-hidden />
            Adicionar ao carrinho
          </Button>
          <Button variant="secondary">Ver detalhes</Button>
          <Button variant="ghost">Cancelar</Button>
          <Button variant="danger">Cancelar pedido</Button>
          <Button disabled>Indisponível</Button>
          <Button variant="secondary" size="icon" aria-label="Buscar">
            <Search aria-hidden />
          </Button>
          <Button variant="secondary" size="icon" aria-label="Minha conta">
            <User aria-hidden />
          </Button>
        </div>
      </Section>

      <Section title="Selos">
        <div className="flex flex-wrap gap-2">
          <Badge variant="deal">Black Friday</Badge>
          <Badge variant="campaign">Black Friday</Badge>
          <Badge variant="success">Pedido confirmado</Badge>
          <Badge variant="danger">Cancelado: sem estoque</Badge>
          <Badge variant="neutral">Aguardando pagamento</Badge>
          <Badge variant="outline">Frete grátis</Badge>
        </div>
      </Section>

      <Section title="Avisos">
        <ToastDemo />
      </Section>

      <Section title="Estados">
        <div className="grid gap-x-10 sm:grid-cols-3">
          <StateMessage
            headingLevel={3}
            illustration={<EmptyCartIllustration />}
            title="Seu carrinho está vazio"
            description="Os produtos que você adicionar aparecem aqui."
            action={<Button variant="secondary">Ver ofertas</Button>}
          />
          <StateMessage
            headingLevel={3}
            illustration={<OrderConfirmedIllustration />}
            title="Pedido confirmado"
            description="O pagamento foi aprovado e o pedido já está sendo separado."
          />
          <StateMessage
            headingLevel={3}
            tone="error"
            illustration={<NotFoundIllustration />}
            title="Não encontramos essa página"
            description="O endereço pode ter mudado. Volte para a vitrine e busque o produto."
            action={<Button variant="secondary">Ir para a vitrine</Button>}
          />
        </div>
      </Section>

      <Section title="Categorias">
        <ul className="grid grid-cols-4 gap-4 sm:grid-cols-7">
          {categories.map((slug) => (
            <li key={slug} className="text-ink-soft flex flex-col items-center gap-2 text-xs">
              <CategoryIcon slug={slug} className="text-ink size-7" />
              {categoryNames[slug]}
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Cores">
        <ul className="grid grid-cols-2 gap-4 sm:grid-cols-4">
          {palette.map(([name, bg, use]) => (
            <li key={name} className="space-y-2">
              <div className={`${bg} h-14 rounded-md border`} />
              <p className="text-sm font-medium">{name}</p>
              <p className="text-ink-soft text-xs">{use}</p>
            </li>
          ))}
        </ul>
      </Section>

      <Section title="Tipografia">
        <div className="space-y-2">
          <p className="text-4xl font-bold tracking-tight">Ofertas da semana</p>
          <p className="text-2xl font-semibold tracking-tight">Eletrônicos e informática</p>
          <p className="text-lg">Entrega para todo o Brasil e troca em até 7 dias.</p>
          <p className="text-ink-soft">
            Archivo em largura normal na interface; estreita e pesada nos preços.
          </p>
          <p className="font-condensed text-4xl font-extrabold">R$ 2.639,90</p>
        </div>
      </Section>
    </main>
  );
}
