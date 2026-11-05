import AxeBuilder from "@axe-core/playwright";
import { expect, test, type Page } from "@playwright/test";

/**
 * O tema da loja: a escolha no cabeçalho (ou no rodapé, no celular) vence o aparelho, sobrevive ao
 * recarregar sem a página nascer no tema errado, e "como o aparelho" volta a seguir o sistema.
 */
const html = (page: Page) => page.locator("html");
const menu = (page: Page) => page.locator(".theme-popover");

test.describe("tema", () => {
  test.use({ colorScheme: "light" });

  test("escolhido no cabeçalho, vence o aparelho e continua depois de recarregar", async ({ page }) => {
    await page.goto("/");
    const button = page.getByRole("button", { name: "Tema: Como o aparelho" });
    await button.click();
    await menu(page).getByRole("radio", { name: "Escuro" }).check({ force: true });
    await expect(html(page)).toHaveClass(/\bdark\b/);
    await expect(page.locator(".theme-popover")).not.toBeVisible();

    // o script do <head> põe a classe antes de qualquer JavaScript da página (aqui, os chunks bloqueados):
    // a página já nasce escura, sem esperar a hidratação
    await page.route(/\/_next\/static\/chunks\/.*\.js(\?|$)/, (route) => route.abort());
    await page.reload();
    await expect(html(page)).toHaveClass(/\bdark\b/);
    const bodyColor = await page.evaluate(() => getComputedStyle(document.body).backgroundColor);
    expect(bodyColor).toBe("rgb(18, 20, 42)"); // o papel do escuro, #12142a
    await page.unroute(/\/_next\/static\/chunks\/.*\.js(\?|$)/);

    await page.reload();
    await page.getByRole("button", { name: "Tema: Escuro" }).click();
    await menu(page).getByRole("radio", { name: "Como o aparelho" }).check({ force: true });
    await expect(html(page)).not.toHaveClass(/\b(dark|light)\b/);
    expect(await page.evaluate(() => localStorage.getItem("tema"))).toBeNull();
  });

  test("pelo teclado: as setas mostram o tema sem fechar, o Esc devolve o foco", async ({ page }) => {
    await page.goto("/");
    await page.getByRole("button", { name: /Tema:/ }).click();
    await page.keyboard.press("Tab"); // entra no grupo, na opção em uso
    await expect(menu(page).getByRole("radio", { name: "Como o aparelho" })).toBeFocused();
    await page.keyboard.press("ArrowUp");
    await expect(html(page)).toHaveClass(/\bdark\b/);
    await expect(page.locator(".theme-popover")).toBeVisible();
    await page.keyboard.press("Escape");
    await expect(page.locator(".theme-popover")).not.toBeVisible();
    await expect(page.getByRole("button", { name: "Tema: Escuro" })).toBeFocused();
  });

  test("no celular, o cabeçalho não tem o botão e o rodapé tem as opções", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/");
    await expect(page.getByRole("button", { name: /Tema:/ })).toBeHidden();
    expect(await page.evaluate(() => document.documentElement.scrollWidth - innerWidth)).toBe(0);
    const footer = page.getByRole("contentinfo").getByRole("group", { name: "Tema da loja" });
    await footer.getByText("Escuro", { exact: true }).click();
    await expect(html(page)).toHaveClass(/\bdark\b/);
    await expect(footer.getByRole("radio", { name: "Escuro" })).toBeChecked();
  });

  for (const scheme of ["light", "dark"] as const) {
    test(`menu aberto sem violações do axe no tema ${scheme === "light" ? "claro" : "escuro"}`, async ({
      page,
    }) => {
      await page.emulateMedia({ colorScheme: scheme });
      await page.goto("/");
      await page.getByRole("button", { name: /Tema:/ }).click();
      await expect(page.locator(".theme-popover")).toBeVisible();
      const results = await new AxeBuilder({ page })
        .include(".theme-popover")
        .include("header")
        .withTags(["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "wcag22aa"])
        .analyze();
      expect(results.violations.map((v) => v.id)).toEqual([]);
    });
  }
});
