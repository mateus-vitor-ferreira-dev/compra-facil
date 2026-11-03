import { expect, test, type Page } from "@playwright/test";

const TV = "/produto/0199f6a0-0000-7000-8000-000000000000";
const NOTEBOOK = "/produto/0199f6a0-0000-7000-8000-000000000002"; // 3 unidades no stub
const SOLD_OUT = "/produto/0199f6a0-0000-7000-8000-000000000032";
const FONE = "/produto/0199f6a0-0000-7000-8000-000000000001";

/** Põe o produto da página no carrinho, com o formulário já hidratado. */
async function add(page: Page, path: string) {
  await page.goto(path);
  await page.locator("form[action] input[name=quantity]").waitFor();
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Adicionado ao carrinho" })).toBeVisible();
}

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
  // a linha (e o botão focado) some: o foco vai para o "Desfazer", e não para o começo da página
  await expect(page.getByRole("button", { name: "Desfazer" })).toBeFocused();
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
  const more = page.getByRole("button", { name: /Aumentar a quantidade de Notebook/ });
  await expect(more).toHaveAttribute("aria-disabled", "true");
  // no limite, o clique não muda nada e o foco continua no botão
  await more.focus();
  await page.keyboard.press("Enter");
  await expect(page.getByRole("status", { name: /Quantidade de Notebook/ })).toHaveText("3");
  await expect(more).toBeFocused();
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

test("com vários produtos, cada linha diz como desistir de um só", async ({ page }) => {
  await add(page, TV);
  await add(page, FONE);
  await page.goto("/carrinho");
  const list = page.getByRole("list", { name: "Produtos no carrinho" });
  // a ação é escrita em cada linha, e não só um ícone
  await expect(list.getByRole("button", { name: /^Remover / })).toHaveCount(2);
  await expect(list.getByRole("listitem").first().getByText("Remover", { exact: true })).toBeVisible();

  await page
    .getByRole("button", { name: "Remover Fone Bluetooth com cancelamento de ruído do carrinho" })
    .click();
  await expect(page.getByRole("button", { name: "Desfazer" })).toBeFocused();
  await expect(list).toContainText("Smart TV 55");
  await expect(list).not.toContainText("Fone Bluetooth");

  await page.getByRole("button", { name: "Desfazer" }).click();
  await expect(list).toContainText("Fone Bluetooth");
  await expect(list).toContainText("Smart TV 55");
});

test("esvaziar aparece só a partir de 3 produtos diferentes, e pede confirmação", async ({ page }) => {
  // 3 unidades do mesmo produto contam como um: sem o botão
  await page.goto(TV);
  const qty = page.getByRole("spinbutton", { name: "Quantidade" });
  await qty.fill("3");
  await qty.blur();
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Adicionado ao carrinho" })).toBeVisible();
  await page.goto("/carrinho");
  await expect(page.getByRole("link", { name: "Esvaziar o carrinho" })).toHaveCount(0);

  await add(page, FONE);
  await page.goto("/carrinho");
  await expect(page.getByRole("link", { name: "Esvaziar o carrinho" })).toHaveCount(0);

  await add(page, NOTEBOOK);
  await page.goto("/carrinho");
  await page.getByRole("link", { name: "Esvaziar o carrinho" }).click();
  // a pergunta recebe o foco; manter não muda nada
  await expect(page.getByRole("heading", { name: "Tirar os 3 produtos do carrinho?" })).toBeFocused();
  await page.getByRole("link", { name: "Manter os produtos" }).click();
  await expect(page.getByRole("list", { name: "Produtos no carrinho" }).getByRole("listitem")).toHaveCount(3);

  await page.getByRole("link", { name: "Esvaziar o carrinho" }).click();
  await page.getByRole("button", { name: "Esvaziar o carrinho" }).click();
  await expect(page.getByRole("heading", { name: "Seu carrinho está vazio" })).toBeVisible();
  await expect(page.getByRole("status").filter({ hasText: "Os produtos saíram do carrinho." })).toBeVisible();
  await expect(page.getByRole("link", { name: "Ver as ofertas" })).toBeVisible();
  await expect(page.getByRole("link", { name: /^Carrinho/ }).first()).not.toContainText("3");
});

test("sem JavaScript, esvaziar também pede confirmação", async ({ browser }) => {
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  for (const path of [TV, FONE, NOTEBOOK]) {
    await page.goto(path);
    await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  }
  await page.goto("/carrinho");
  await page.getByRole("link", { name: "Esvaziar o carrinho" }).click();
  await page.getByRole("button", { name: "Esvaziar o carrinho" }).click();
  await expect(page.getByRole("heading", { name: "Seu carrinho está vazio" })).toBeVisible();
  expect((await context.cookies()).find((c) => c.name === "cf_cart")).toBeUndefined();
  await context.close();
});
