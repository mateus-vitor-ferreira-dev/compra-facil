import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

/**
 * A visão geral do painel (#382): a entrada do painel, com o extrato do período (a soma fecha no que sobrou),
 * as vendas por dia, como pagaram e os mais vendidos. O período fica na URL e vale para tudo.
 */
async function signIn(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);
}

const cents = (text: string) => {
  const negative = text.includes("−");
  const value = Number(text.replace(/[^\d,]/g, "").replace(",", "."));
  return Math.round(value * 100) * (negative ? -1 : 1);
};

test("o painel abre na visão geral, e o extrato fecha a conta", async ({ page }) => {
  await signIn(page, "/admin");
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Visão geral");
  await expect(
    page.getByRole("navigation", { name: "Seções do painel" }).getByRole("link", { name: "Visão geral" }),
  ).toHaveAttribute("aria-current", "page");
  // sem período na URL, os últimos 30 dias
  await expect(
    page.getByRole("navigation", { name: "Período" }).getByRole("link", { name: "30 dias" }),
  ).toHaveAttribute("aria-current", "page");

  const extrato = page.getByRole("region", { name: "Extrato do período" });
  const rows = extrato.getByRole("row");
  await expect(rows.first()).toContainText("Vendas brutas");
  await expect(extrato.getByRole("row", { name: /Estornos/ })).toContainText("− R$ 99,00");
  const values = await rows.locator("td").allTextContents();
  const left = await extrato.locator("p.font-condensed").textContent();
  expect(values.map(cents).reduce((s, c) => s + c, 0)).toBe(cents(left ?? ""));

  const pagaram = page.getByRole("region", { name: "Como pagaram" });
  await expect(pagaram).toContainText("Cartão");
  await expect(pagaram).toContainText("86% das vendas");
  await expect(
    page.getByRole("region", { name: "Mais vendidos" }).getByRole("link", { name: "Fone Sonix" }),
  ).toHaveAttribute("href", "/admin/produtos/0199f6a0-0000-7000-8000-000000000001");
});

test("o gráfico por dia se lê pelo teclado e tem a tabela com os números", async ({ page }) => {
  await signIn(page, "/admin?de=2026-10-29&ate=2026-11-04");
  const chart = page.getByRole("group", { name: /Vendas brutas por dia/ });
  const last = chart.getByRole("button").last();
  await expect(last).toHaveAccessibleName(/R\$\s369,00 em 3 pedidos pagos, R\$\s99,00 estornados/);
  await last.focus();
  await page.keyboard.press("ArrowLeft");
  await expect(chart.getByRole("button").nth(-2)).toBeFocused();
  await page.keyboard.press("End");
  await expect(last).toBeFocused();
  // um só ponto de Tab no gráfico: as setas andam entre os dias
  await expect(chart.locator('button[tabindex="0"]')).toHaveCount(1);

  await page.getByText("Ver os números por dia").click();
  await expect(page.getByRole("table").filter({ hasText: "Pedidos pagos" }).getByRole("row")).toHaveCount(8);
});

test("período inválido avisa e mostra os últimos 30 dias", async ({ page }) => {
  await signIn(page, "/admin?de=2024-01-01&ate=2026-11-04");
  await expect(page.getByRole("alert").filter({ hasText: "366 dias" })).toContainText(
    "O período vai até 366 dias.",
  );
  await expect(page.getByLabel("De", { exact: true })).toHaveValue("2024-01-01");
});

test("celular: sem rolagem para o lado", async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await signIn(page, "/admin");
  await expect(page.getByRole("region", { name: "Extrato do período" })).toBeVisible();
  expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0);
});

for (const scheme of ["light", "dark"] as const) {
  test(`visão geral sem violações do axe no tema ${scheme === "light" ? "claro" : "escuro"}`, async ({
    page,
  }) => {
    await page.emulateMedia({ colorScheme: scheme });
    await signIn(page, "/admin");
    const results = await new AxeBuilder({ page })
      .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
      .analyze();
    expect(results.violations.map((v) => `${v.id}: ${v.nodes.map((n) => n.target).join(" ")}`)).toEqual([]);
  });
}
