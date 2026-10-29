import { expect, test, type Page } from "@playwright/test";

async function signIn(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
}

test("margem do fornecedor: a conta ao vivo, a regra padrão, a de categoria e a leitura do feed", async ({
  page,
}) => {
  await signIn(page, "/admin/fornecedor");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Fornecedor");

  const padrao = page.getByRole("region", { name: "Margem padrão" });
  const conta = padrao.getByRole("list", { name: "A conta do preço" });
  // US$ 12,40 × 5,40 = R$ 66,96; +80% = R$ 120,53 → R$ 120,90
  await expect(conta).toContainText("R$ 66,96");
  await expect(conta).toContainText("R$ 120,90");
  await padrao.getByLabel("Margem (%)").fill("100");
  await expect(conta).toContainText("R$ 134,90"); // R$ 133,92 sobe para o próximo ,90
  await padrao.getByRole("button", { name: "Gravar a margem padrão" }).click();
  await expect(page.getByRole("status")).toHaveText("Regra gravada: 1 produto mudou de preço.");

  const categorias = page.getByRole("region", { name: "Por categoria" });
  await categorias.getByLabel("Categoria").selectOption({ index: 2 });
  const escolhida = await categorias.getByLabel("Categoria").locator("option:checked").textContent();
  await categorias.getByLabel("Margem (%)").fill("120");
  await categorias.getByRole("button", { name: "Gravar a regra da categoria" }).click();
  await expect(page.getByRole("status")).toContainText("Regra gravada");
  const regra = categorias
    .getByRole("listitem")
    .filter({ hasText: escolhida!.replace(" (o departamento todo)", "") });
  await expect(regra).toContainText("120% de margem");
  await regra.getByRole("button", { name: /Remover/ }).click();
  await expect(page.getByRole("status")).toHaveText("Regra removida: 1 produto mudou de preço.");

  await page.getByRole("button", { name: "Ler o feed agora" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Feed lido: 2 novos, 1 alterado e 0 saíram do feed. A loja atualiza em segundos.",
  );
});

test("o produto do fornecedor mostra o custo e de onde vem o preço", async ({ page }) => {
  await signIn(page, "/admin/produtos/0199f6a0-0000-7000-8000-000000000063");
  const bloco = page.getByRole("region", { name: "Do fornecedor" });
  await expect(bloco).toContainText("SIM-V-004");
  await expect(bloco).toContainText("US$ 18,90");
  await expect(bloco.getByRole("link", { name: "margem do fornecedor" })).toHaveAttribute(
    "href",
    "/admin/fornecedor",
  );
});
