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

test("a categoria mãe inclui as subcategorias e o 'Ver mais' traz a próxima página", async ({ page }) => {
  await page.goto("/categoria/eletronicos");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Eletrônicos");
  await expect(page.getByRole("navigation", { name: "Subcategorias de Eletrônicos" })).toContainText(
    "Televisores",
  );

  const grid = page.getByRole("list", { name: "Produtos em Eletrônicos" });
  await expect(grid.locator("article")).toHaveCount(24);
  await page.getByRole("link", { name: "Ver mais produtos" }).click();
  await expect(grid.locator("article")).toHaveCount(32);
  await expect(page.getByRole("link", { name: "Ver mais produtos" })).toHaveCount(0);
  // o foco foi para o primeiro produto novo
  await expect(page.locator(":focus")).toHaveText(/Fone de ouvido modelo/);
});

test("'Só em promoção' fica na URL e filtra a categoria", async ({ page }) => {
  await page.goto("/categoria/tv");
  await page.getByRole("link", { name: "Só em promoção" }).click();
  await expect(page).toHaveURL(/promocao=1/);
  await expect(page.getByRole("link", { name: "Só em promoção (ligado)" })).toHaveAttribute(
    "aria-current",
    "true",
  );

  await page.goto("/categoria/fones?promocao=1");
  await expect(
    page.getByRole("heading", { name: "Nada em promoção em Fones de ouvido agora" }),
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
