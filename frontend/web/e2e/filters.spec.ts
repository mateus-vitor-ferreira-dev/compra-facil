import { expect, test } from "@playwright/test";

const FONES = "/categoria/fones";

test("marca, ordem e preço aplicam na hora e ficam na URL", async ({ page }) => {
  await page.goto(FONES);
  const filters = page.getByRole("complementary", { name: "Filtrar e ordenar" });

  await filters.getByRole("checkbox", { name: /^JBL/ }).check();
  await expect(page).toHaveURL(/marca=JBL/);
  const grid = page.getByRole("list", { name: "Produtos em Fones de ouvido" });
  await expect(grid.getByRole("listitem")).toHaveCount(10);

  await filters.getByRole("radio", { name: "Menor preço" }).check();
  await expect(page).toHaveURL(/ordem=menor-preco/);
  await expect(grid.getByRole("heading").first()).toHaveText("Fone de ouvido modelo 3");

  await filters.getByLabel("Até, em reais").fill("110");
  await filters.getByRole("button", { name: "Aplicar filtros" }).click();
  await expect(page).toHaveURL(/preco-max=110/);
  await expect(page).not.toHaveURL(/preco-min=/); // campo vazio não vai para a URL
  await expect(grid.getByRole("listitem")).toHaveCount(3);

  const active = page.getByRole("navigation", { name: "Filtros ligados" });
  await expect(active.getByRole("link", { name: "Até R$ 110,00 (tirar o filtro)" })).toBeVisible();
  await active.getByRole("link", { name: "JBL (tirar o filtro)" }).click();
  await expect(page).not.toHaveURL(/marca=/);
  await expect(filters.getByRole("checkbox", { name: /^JBL/ })).not.toBeChecked();

  await page.goto(`${FONES}?marca=JBL&preco-max=110`);
  await page.getByRole("link", { name: "Limpar filtros" }).first().click();
  await expect(page).toHaveURL(new RegExp(`${FONES}$`));
});

test("a marca mostra quantos produtos tem, e filtro sem resultado oferece limpar", async ({ page }) => {
  await page.goto(`${FONES}?preco-min=900`);
  await expect(
    page.getByRole("heading", { name: "Nenhum produto em Fones de ouvido com esses filtros" }),
  ).toBeVisible();
  await page.getByRole("main").getByRole("link", { name: "Limpar filtros" }).last().click();
  const filters = page.getByRole("complementary", { name: "Filtrar e ordenar" });
  await expect(filters.getByRole("checkbox", { name: "Edifier 10 produtos" })).toBeVisible();
});

test("sem JavaScript, o formulário aplica pelo botão", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto(FONES);
  const filters = page.getByRole("complementary", { name: "Filtrar e ordenar" });
  await filters.getByRole("checkbox", { name: /^Xiaomi/ }).check();
  await filters.getByRole("radio", { name: "Maior preço" }).check();
  await filters.getByRole("button", { name: "Aplicar filtros" }).click();
  await expect(page).toHaveURL(/marca=Xiaomi/);
  await expect(page).toHaveURL(/ordem=maior-preco/);
  await expect(
    page.getByRole("list", { name: "Produtos em Fones de ouvido" }).getByRole("heading").first(),
  ).toHaveText("Fone de ouvido modelo 29");
  await context.close();
});

test("no celular, a folha mostra quantos filtros estão ligados", async ({ browser }) => {
  const context = await browser.newContext({ viewport: { width: 390, height: 844 } });
  const page = await context.newPage();
  await page.goto(`${FONES}?marca=JBL&promocao=1&ordem=menor-preco`);
  const sheet = page.locator("summary").filter({ hasText: "Filtrar e ordenar" });
  await expect(sheet).toContainText("2 filtros ligados");
  await sheet.click();
  // o botão da folha diz quantos produtos vai mostrar (Baymard), e fica preso no rodapé
  await expect(page.getByRole("button", { name: /^Ver \d+ produtos?$/ })).toBeVisible();
  await context.close();
});

test("os tipos da subcategoria filtram, e a subcategoria continua marcada (#210)", async ({ page }) => {
  await page.goto(FONES);
  const types = page.getByRole("navigation", { name: "Tipos de Fones de ouvido" });
  await expect(types.getByRole("link", { name: "Tudo em Fones de ouvido" })).toHaveAttribute(
    "aria-current",
    "page",
  );

  await types.getByRole("link", { name: "Fones sem fio" }).click();
  await expect(page).toHaveURL(/\/categoria\/fones-sem-fio$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Fones sem fio");
  const grid = page.getByRole("list", { name: "Produtos em Fones sem fio" });
  await expect(grid.getByRole("listitem")).toHaveCount(1);
  await expect(types.getByRole("link", { name: "Fones sem fio" })).toHaveAttribute("aria-current", "page");
  // no nível de cima, a subcategoria do tipo segue destacada, e o caminho tem os três níveis
  const subs = page.getByRole("navigation", { name: "Subcategorias de Eletrônicos" });
  await expect(subs.getByRole("link", { name: "Fones de ouvido" })).toBeVisible();
  const crumbs = page.getByRole("navigation", { name: "Você está em" });
  await expect(crumbs.getByRole("link", { name: "Fones de ouvido" })).toHaveAttribute(
    "href",
    "/categoria/fones",
  );
});

test("a faceta de tipo combina tipos, com contagem, e as marcas acompanham (#212)", async ({ page }) => {
  await page.goto(FONES);
  const filters = page.getByRole("complementary", { name: "Filtrar e ordenar" });
  const tipo = filters.getByRole("group", { name: "Tipo" });
  await expect(tipo.getByRole("checkbox")).toHaveCount(2);

  await tipo.getByRole("checkbox", { name: /^Headsets gamer/ }).check();
  await expect(page).toHaveURL(/tipo=headsets-gamer/);
  const grid = page.getByRole("list", { name: "Produtos em Fones de ouvido" });
  await expect(grid.getByRole("listitem")).toHaveCount(1);
  // a marca conta só o que sobrou no tipo
  await expect(filters.getByRole("checkbox", { name: /^HyperX/ })).toBeVisible();
  await expect(filters.getByRole("checkbox", { name: /^JBL/ })).toHaveCount(0);

  await tipo.getByRole("checkbox", { name: /^Fones sem fio/ }).check();
  await expect(grid.getByRole("listitem")).toHaveCount(2);
  const active = page.getByRole("navigation", { name: "Filtros ligados" });
  await active.getByRole("link", { name: "Headsets gamer (tirar o filtro)" }).click();
  await expect(page).not.toHaveURL(/headsets-gamer/);
  await expect(grid.getByRole("listitem")).toHaveCount(1);
});
