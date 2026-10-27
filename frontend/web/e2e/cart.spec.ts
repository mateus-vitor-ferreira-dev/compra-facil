import { expect, test } from "@playwright/test";

const TV = "/produto/0199f6a0-0000-7000-8000-000000000000";
const NOTEBOOK = "/produto/0199f6a0-0000-7000-8000-000000000002"; // 3 unidades no stub
const SOLD_OUT = "/produto/0199f6a0-0000-7000-8000-000000000032";

test("a página do produto mostra preço de campanha, prazo e estoque", async ({ page }) => {
  await page.goto(TV);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText('Smart TV 55" QLED');
  await expect(page.getByText("de R$ 3.299,90 por R$ 2.639,92, 20% de desconto")).toBeAttached();
  await expect(page.getByText("Black Friday: 20% de desconto neste produto")).toBeVisible();
  await expect(page.getByText("Em estoque")).toBeVisible();
  await expect(page.getByRole("navigation", { name: "Você está em" })).toContainText("Eletrônicos");

  const ld = JSON.parse((await page.locator('script[type="application/ld+json"]').textContent()) ?? "{}");
  expect(ld).toMatchObject({
    "@type": "Product",
    offers: { price: "2639.92", availability: "https://schema.org/InStock" },
  });
});

test("produto inexistente ou id inválido é 404", async ({ page }) => {
  expect((await page.goto("/produto/0199f6a0-0000-7000-8000-0000000fffff"))?.status()).toBe(404);
  expect((await page.goto("/produto/nao-e-um-id"))?.status()).toBe(404);
});

test("esgotado não tem botão de compra", async ({ page }) => {
  await page.goto(SOLD_OUT);
  await expect(page.getByText("Esgotado")).toBeVisible();
  await expect(page.getByRole("button", { name: "Adicionar ao carrinho" })).toHaveCount(0);
});

test("adiciona, ajusta e remove no carrinho, com o total e a economia", async ({ page }) => {
  await page.goto(TV);
  await page.locator("form[action] input[name=quantity]").waitFor();
  await page.getByRole("button", { name: "Aumentar a quantidade" }).click();
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(
    page.getByRole("status").filter({ hasText: "Adicionado ao carrinho: 2 unidades" }),
  ).toBeVisible();
  await expect(page.getByRole("link", { name: /^Carrinho com 2 itens$/ })).toBeVisible();

  await page.getByRole("link", { name: "Ver o carrinho" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Carrinho");
  const summary = page.getByRole("complementary", { name: "Resumo" });
  await expect(summary).toContainText("R$ 5.279,84");
  await expect(summary).toContainText("Você economiza R$ 1.319,96");

  await page.getByRole("button", { name: 'Diminuir a quantidade de Smart TV 55" QLED' }).click();
  await expect(page.getByRole("status", { name: 'Quantidade de Smart TV 55" QLED' })).toHaveText("1");
  await expect(summary).toContainText("R$ 2.639,92");

  await page.getByRole("button", { name: 'Remover Smart TV 55" QLED do carrinho' }).click();
  await expect(page.getByRole("heading", { name: "Seu carrinho está vazio" })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "saiu do carrinho" })).toContainText("Smart TV 55");

  await page.getByRole("button", { name: "Desfazer" }).click();
  await expect(page.getByRole("list", { name: "Produtos no carrinho" })).toContainText("Smart TV 55");
  await expect(page.getByRole("button", { name: "Desfazer" })).toHaveCount(0);
});

test("não passa do estoque: o limite vem no aviso e no +", async ({ page }) => {
  await page.goto(NOTEBOOK);
  const qty = page.getByRole("spinbutton", { name: "Quantidade" });
  await qty.fill("10");
  await qty.blur();
  await expect(qty).toHaveValue("3");
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "3 unidades no total" })).toBeVisible();

  await page.goto("/carrinho");
  await expect(page.getByRole("button", { name: /Aumentar a quantidade de Notebook/ })).toBeDisabled();
});

test("sem JavaScript o carrinho funciona do mesmo jeito", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto(TV);
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await page.goto("/carrinho");
  await expect(page.getByRole("list", { name: "Produtos no carrinho" })).toContainText("Smart TV 55");
  await page.getByRole("button", { name: /Aumentar a quantidade de Smart TV/ }).click();
  await expect(page.getByRole("status", { name: /Quantidade de Smart TV/ })).toHaveText("2");
  const cookie = (await context.cookies()).find((c) => c.name === "cf_cart");
  expect(cookie?.httpOnly).toBe(true);
  await context.close();
});
