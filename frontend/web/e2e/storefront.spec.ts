import { expect, test } from "@playwright/test";

test("a home destaca a campanha vigente e lista o que está em promoção", async ({ page }) => {
  await page.goto("/");

  const hero = page.getByRole("region", { name: "Black Friday" });
  await expect(hero).toContainText("Até 20% de desconto em 2 produtos");
  await expect(hero.getByText(/Termina em 2 dias, 23 horas/)).toBeAttached();

  const onSale = page.getByRole("list", { name: "Produtos em promoção" });
  await expect(onSale.getByRole("listitem")).toHaveCount(2);
  await expect(onSale).toContainText("Smart TV 55");
  await expect(onSale).toContainText("de R$ 3.299,90 por R$ 2.639,92, 20% de desconto");
});

test("a categoria mãe inclui as subcategorias, com Carregar mais (#212)", async ({ page }) => {
  await page.goto("/categoria/eletronicos");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Eletrônicos");
  await expect(page.getByRole("navigation", { name: "Subcategorias de Eletrônicos" })).toContainText(
    "Televisores",
  );

  const grid = page.getByRole("list", { name: "Produtos em Eletrônicos" });
  await expect(grid.locator("article")).toHaveCount(48);
  await expect(page.getByText("Você viu 48 de 63 produtos")).toBeVisible();

  // o lote novo entra na mesma tela, a URL acompanha e o foco vai ao primeiro card novo
  await page.getByRole("link", { name: "Carregar mais 15 produtos" }).click();
  await expect(page).toHaveURL(/\/categoria\/eletronicos\?pagina=2$/);
  await expect(grid.locator("article")).toHaveCount(63);
  await expect(page.getByText("Você viu 63 de 63 produtos")).toBeVisible();
  await expect(page.getByRole("link", { name: /^Carregar mais/ })).toHaveCount(0); // acabou
  await expect(page.locator('[data-lote="2"] a').first()).toBeFocused();

  // o Voltar da página do produto devolve à lista com os dois lotes
  await page.locator('[data-lote="2"] a').first().click();
  await expect(page).toHaveURL(/\/produto\//);
  await page.goBack();
  await expect(page).toHaveURL(/\?pagina=2$/);
  await expect(grid.locator("article")).toHaveCount(63);

  // trocar um filtro volta ao primeiro lote
  await page
    .getByRole("complementary", { name: "Filtrar e ordenar" })
    .getByRole("radio", { name: "Menor preço" })
    .check();
  await expect(page).toHaveURL(/ordem=menor-preco/);
  await expect(page).not.toHaveURL(/pagina=/);
});

test("Carregar mais funciona sem JavaScript, pelo link", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("/categoria/eletronicos");
  await page.getByRole("link", { name: "Carregar mais 15 produtos" }).click();
  await expect(page).toHaveURL(/\?pagina=2$/);
  await expect(page.getByRole("list", { name: "Produtos em Eletrônicos" }).locator("article")).toHaveCount(
    63,
  );
  await context.close();
});

test("um ponto da lista que não existe mais explica e volta ao começo", async ({ page }) => {
  await page.goto("/categoria/eletronicos?pagina=20");
  await expect(page.getByRole("heading", { name: "Esta listagem tem 63 produtos" })).toBeVisible();
  await page.getByRole("link", { name: "Ver desde o começo" }).click();
  await expect(page).toHaveURL(/\/categoria\/eletronicos$/);
});

test("'Só em promoção' fica na URL e filtra a categoria", async ({ page }) => {
  await page.goto("/categoria/tv");
  const filters = page.getByRole("complementary", { name: "Filtrar e ordenar" });
  await filters.getByRole("checkbox", { name: "Só em promoção" }).check();
  await expect(page).toHaveURL(/promocao=1/);
  await expect(
    page
      .getByRole("navigation", { name: "Filtros ligados" })
      .getByRole("link", { name: "Só em promoção (tirar o filtro)" }),
  ).toBeVisible();

  await page.goto("/categoria/fones?promocao=1");
  await expect(
    page.getByRole("heading", { name: "Nenhum produto em Fones de ouvido com esses filtros" }),
  ).toBeVisible();
});

test("categoria inexistente é 404", async ({ page }) => {
  const res = await page.goto("/categoria/nao-existe");
  expect(res?.status()).toBe(404);
  await expect(page.getByRole("heading", { name: "Esta página não existe" })).toBeVisible();
});

test("busca pelo cabeçalho: sugestões pelo teclado e a página de resultados", async ({ page }) => {
  await page.goto("/");
  const box = page.getByRole("combobox", { name: "Buscar produtos" });
  await page.locator("form[role=search][data-ready]").waitFor(); // sugestões só depois da hidratação
  await box.fill("note");
  await expect(page.getByRole("option", { name: /Categoria: Notebooks/ })).toBeVisible();

  await box.press("Enter"); // nada marcado: busca o texto
  await expect(page).toHaveURL(/\/busca\?q=note/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Resultados para “note”");
  await expect(page.getByRole("combobox", { name: "Buscar produtos" })).toHaveValue("note");
  await expect(page.getByRole("list", { name: "Resultados para note" }).locator("article")).toHaveCount(1);

  await page.goto("/busca?q=geladeira");
  await expect(
    page.getByRole("heading", { name: "Nenhum produto encontrado para “geladeira”" }),
  ).toBeVisible();
});

test("limite de requisições (429) explica quanto esperar", async ({ page }) => {
  await page.goto("/busca?q=limite");
  const alert = page.getByRole("alert").filter({ hasText: "Muitas buscas em pouco tempo" });
  await expect(alert).toContainText("Espere 42 segundos");
});

test("sitemap lista as categorias", async ({ request }) => {
  const xml = await (await request.get("/sitemap.xml")).text();
  expect(xml).toContain("/categoria/fones</loc>");
  expect(xml).toContain("/ofertas</loc>");
});

test("galeria do produto: miniaturas e setas trocam a foto", async ({ page }) => {
  await page.goto("/produto/0199f6a0-0000-7000-8000-000000000000");
  const fotos = page.getByRole("group", { name: /^Fotos de / });
  const rail = fotos.getByRole("group", { name: /Foto em destaque/ });
  const left = () => rail.evaluate((el) => Math.round(el.scrollLeft / el.clientWidth));

  const third = fotos.getByRole("button", { name: "Ver a foto 3 de 3" });
  await third.click();
  await expect(third).toHaveAttribute("aria-current", "true");
  await expect.poll(left).toBe(2);

  await third.press("ArrowLeft");
  const second = fotos.getByRole("button", { name: "Ver a foto 2 de 3" });
  await expect(second).toBeFocused();
  await expect.poll(left).toBe(1);
  await expect(fotos.getByText("Foto 2 de 3")).toBeAttached();
});

test("galeria no celular: arrastar a faixa marca a miniatura", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 800 });
  await page.goto("/produto/0199f6a0-0000-7000-8000-000000000000");
  const fotos = page.getByRole("group", { name: /^Fotos de / });
  await fotos.getByRole("group", { name: /Foto em destaque/ }).evaluate((el) => {
    el.scrollBy({ left: el.clientWidth, behavior: "instant" });
  });
  await expect(fotos.getByRole("button", { name: "Ver a foto 2 de 3" })).toHaveAttribute(
    "aria-current",
    "true",
  );
});

test("as políticas abrem pelo rodapé, marcadas como rascunho e fora dos buscadores", async ({ page }) => {
  await page.goto("/");
  for (const name of ["Entrega", "Trocas e devoluções", "Privacidade", "Termos de uso"]) {
    await page.getByRole("navigation", { name: "Ajuda" }).getByRole("link", { name }).click();
    await expect(page.getByRole("heading", { level: 1 })).toHaveText(name);
    await expect(page.getByRole("note")).toContainText("Rascunho em revisão");
    await expect(page.locator('meta[name="robots"]')).toHaveAttribute("content", /noindex/);
  }
});

test("sem dados configurados, a loja não inventa quem vende", async ({ page }) => {
  await page.goto("/politicas/termos");
  await expect(page.getByText("[Nome empresarial, CNPJ e endereço a definir.]")).toBeVisible();
  await expect(page.locator("address")).toHaveCount(0);
});

test("menu de categorias que não cabe: a borda esmaece, a seta rola e o foco traz o item à vista (#317)", async ({
  page,
}) => {
  await page.setViewportSize({ width: 1024, height: 768 });
  await page.goto("/");
  const nav = page.getByRole("banner").getByRole("navigation", { name: "Categorias" });
  const list = nav.locator("ul").last();
  // o stub tem 2 departamentos, que cabem: sem seta
  await expect(nav.locator("button")).toHaveCount(0);

  // departamentos a mais, como numa loja maior (depois da hidratação, que liga a leitura da rolagem)
  await page.waitForLoadState("networkidle");
  await list.evaluate((ul) => {
    for (let i = 1; i <= 14; i++) {
      const li = document.createElement("li");
      li.className = "shrink-0";
      li.innerHTML = `<a href="/categoria/extra-${i}" class="flex h-10 items-center text-sm whitespace-nowrap">Departamento extra ${i}</a>`;
      ul.append(li);
    }
    ul.dispatchEvent(new Event("scroll"));
  });
  const next = nav.locator("button").last();
  await expect(next).toBeVisible();
  await next.click();
  await expect.poll(() => list.evaluate((ul) => ul.scrollLeft)).toBeGreaterThan(0);
  await expect(nav.locator("button")).toHaveCount(2);

  // pelo teclado, sem as setas: o último departamento fica à vista quando recebe o foco
  const last = nav.getByRole("link", { name: "Departamento extra 14" });
  await last.focus();
  await expect(last).toBeInViewport({ ratio: 1 });
});
