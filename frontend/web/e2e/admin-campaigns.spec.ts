import { expect, test, type Page } from "@playwright/test";

const local = (offsetMs: number) => new Date(Date.now() + offsetMs - 3 * 3600e3).toISOString().slice(0, 16);

async function signIn(page: Page) {
  await page.goto("/admin/campanhas");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/campanhas$/);
}

async function createCampaign(page: Page, name: string) {
  await page.goto("/admin/campanhas/nova");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Nova campanha");
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Desconto (%)").fill("25");
  await page.getByLabel("Começa em").fill(local(-60e3));
  await page.getByLabel("Termina em").fill(local(3 * 86400e3));
  await page.getByRole("button", { name: "Criar campanha" }).click();
  await expect(page.getByRole("status")).toHaveText("Campanha salva. A vitrine já mostra os preços novos.");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(name);
  return page.url().split("?")[0]!;
}

/** Remove a campanha: o stub guarda as campanhas, e a próxima volta dos testes reusaria os produtos. */
async function removeCampaign(page: Page, url: string, name: string) {
  await page.goto(url);
  await page.getByText("Remover campanha").click();
  await page.getByRole("button", { name: `Remover ${name}` }).click();
  await expect(page.getByRole("status")).toContainText("Campanha removida");
}

test("cria a campanha, inclui produtos pela busca e retira um", async ({ page }) => {
  await signIn(page);
  const name = `Semana do fone ${Date.now() % 100000}`;
  const url = await createCampaign(page, name);
  await expect(page.getByText("Nenhum produto ainda.")).toBeVisible();

  await page.getByLabel("Buscar produtos por nome, marca ou SKU").fill("modelo 1");
  await page.getByRole("button", { name: "Buscar" }).click();
  await page.getByRole("checkbox", { name: /Fone de ouvido modelo 10/ }).check();
  await page.getByRole("checkbox", { name: /Fone de ouvido modelo 11/ }).check();
  await page.getByRole("button", { name: "Incluir os marcados" }).click();
  await expect(page.getByRole("status")).toHaveText("2 produtos incluídos na campanha.");
  await expect(page.getByRole("heading", { name: "Produtos na campanha (2)" })).toBeVisible();

  await page.getByRole("button", { name: "Retirar Fone de ouvido modelo 10 da campanha" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Produto retirado da campanha: volta ao preço de tabela.",
  );
  await expect(page.getByRole("heading", { name: "Produtos na campanha (1)" })).toBeVisible();

  await page
    .getByRole("navigation", { name: "Seções do painel" })
    .getByRole("link", { name: "Campanhas" })
    .click();
  const row = page.getByRole("row", { name: new RegExp(name) });
  await expect(row).toContainText("25%");
  await expect(row).toContainText("No ar");
  await removeCampaign(page, url, name);
});

test("o mesmo produto em duas campanhas no mesmo período é explicado", async ({ page }) => {
  await signIn(page);
  const tag = Date.now() % 100000;
  const first = await createCampaign(page, `Primeira ${tag}`);
  await page.getByLabel("Buscar produtos por nome, marca ou SKU").fill("modelo 20");
  await page.getByRole("button", { name: "Buscar" }).click();
  await page.getByRole("checkbox", { name: /Fone de ouvido modelo 20/ }).check();
  await page.getByRole("button", { name: "Incluir os marcados" }).click();
  await expect(page.getByRole("status")).toHaveText("1 produto incluído na campanha.");

  await createCampaign(page, `Segunda ${tag}`);
  await page.getByLabel("Buscar produtos por nome, marca ou SKU").fill("modelo 20");
  await page.getByRole("button", { name: "Buscar" }).click();
  await page.getByRole("checkbox", { name: /Fone de ouvido modelo 20/ }).check();
  await page.getByRole("button", { name: "Incluir os marcados" }).click();
  const alert = page.getByRole("alert").filter({ hasText: "outra campanha que cruza este período" });
  await expect(alert).toBeVisible();
  await expect(alert.locator("code")).toHaveText("4bf92f3577b34da6a3ce929d0e0e4736");

  await removeCampaign(page, page.url().split("?")[0]!, `Segunda ${tag}`);
  await removeCampaign(page, first, `Primeira ${tag}`);
});

test("o fim antes do início é recusado no formulário", async ({ page }) => {
  await signIn(page);
  await page.goto("/admin/campanhas/nova");
  await page.getByLabel("Nome").fill("Ao contrário");
  await page.getByLabel("Desconto (%)").fill("10");
  await page.getByLabel("Começa em").fill(local(86400e3));
  await page.getByLabel("Termina em").fill(local(0));
  await page.getByRole("button", { name: "Criar campanha" }).click();
  await expect(page.getByText("O fim vem depois do início.")).toBeVisible();
  await expect(page.getByLabel("Termina em")).toHaveAttribute("aria-invalid", "true");
});
