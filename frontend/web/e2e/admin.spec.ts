import { expect, test, type Page } from "@playwright/test";
import { passTwoFactor } from "./two-factor";

async function signIn(page: Page, email: string, path = "/admin/produtos") {
  await page.goto(path);
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  if (email.startsWith("admin@")) await passTwoFactor(page);
  await expect(page).not.toHaveURL(/\/entrar/);
}

async function openProduct(page: Page, name: string) {
  await page.getByLabel("Buscar por nome, marca ou SKU").fill(name);
  await page.getByRole("button", { name: "Buscar" }).click();
  await page.getByRole("link", { name, exact: true }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(name);
}

test("o painel pede login e não existe para quem é cliente", async ({ page }) => {
  // sem sessão, o login traz de volta para a página pedida, com a busca
  await page.goto("/admin/produtos?q=fone");
  await expect(page).toHaveURL(/\/entrar\?voltar=%2Fadmin%2Fprodutos%3Fq%3Dfone$/);

  await signIn(page, "ana@exemplo.com", "/conta");
  await expect(page.getByRole("link", { name: "Abrir o painel" })).toHaveCount(0);
  expect((await page.goto("/admin/produtos"))?.status()).toBe(404);
});

test("edita o preço e ajusta o estoque de um produto", async ({ page }) => {
  await signIn(page, "admin@exemplo.com", "/admin/produtos");
  await expect(page).toHaveURL(/\/admin\/produtos$/);
  await openProduct(page, "Fone de ouvido modelo 28");

  await page.getByLabel("Preço de tabela").fill("149,90");
  await page.getByRole("button", { name: "Salvar alterações" }).click();
  await expect(page.getByRole("status")).toHaveText("Produto salvo. A vitrine já mostra a versão nova.");
  await expect(page.getByLabel("Preço de tabela")).toHaveValue("149,90");

  // o stub guarda os ajustes entre testes: o esperado parte do saldo mostrado agora
  const stock = page.getByRole("complementary").getByRole("definition").first();
  const before = Number(await stock.innerText());
  await page.getByLabel("Unidades").fill("5");
  await page.getByLabel("Motivo").selectOption("restock");
  await page.getByRole("button", { name: "Ajustar estoque" }).click();
  await expect(page.getByRole("status")).toHaveText(`Estoque ajustado: ${before + 5} unidades em estoque.`);

  // a baixa abaixo do que existe é recusada com a explicação
  await page.getByLabel("Unidades").fill("-1000");
  await page.getByLabel("Motivo").selectOption("damage");
  await page.getByRole("button", { name: "Ajustar estoque" }).click();
  await expect(page.getByRole("alert").filter({ hasText: "abaixo do que já está reservado" })).toBeVisible();
});

test("as fotos do produto: acrescenta, reordena e remove", async ({ page }) => {
  await signIn(page, "admin@exemplo.com", "/admin/produtos/0199f6a0-0000-7000-8000-000000000001");
  const fotos = page.getByRole("group", { name: "Fotos" });
  await fotos.getByLabel("Endereço da foto 1").fill("https://cdn.example.com/a.jpg");
  await fotos.getByRole("button", { name: "Adicionar foto" }).click();
  await fotos.getByLabel("Endereço da foto 2").fill("https://cdn.example.com/b.jpg");
  await fotos.getByRole("button", { name: "Adicionar foto" }).click();
  await fotos.getByLabel("Endereço da foto 3").fill("https://cdn.example.com/c.jpg");

  // sobe a segunda, que vira a primeira, e tira a última
  const up = fotos.getByRole("button", { name: "Subir a foto 2" });
  await up.click();
  await expect(fotos.getByLabel("Endereço da foto 1")).toHaveValue("https://cdn.example.com/b.jpg");
  await fotos.getByRole("button", { name: "Remover a foto 3" }).click();
  await expect(fotos.getByRole("textbox")).toHaveCount(2);

  await page.getByRole("button", { name: "Salvar alterações" }).click();
  await expect(page.getByRole("status")).toHaveText("Produto salvo. A vitrine já mostra a versão nova.");
  await expect(page.getByRole("group", { name: "Fotos" }).getByRole("textbox")).toHaveCount(2);
  await expect(page.getByRole("group", { name: "Fotos" }).getByLabel("Endereço da foto 1")).toHaveValue(
    "https://cdn.example.com/b.jpg",
  );
});

test("duas pessoas editando: a segunda é avisada em vez de apagar a mudança da primeira", async ({
  browser,
}) => {
  const context = await browser.newContext();
  const first = await context.newPage();
  await signIn(first, "admin@exemplo.com");
  await openProduct(first, "Fone de ouvido modelo 27");
  const second = await context.newPage();
  await second.goto(first.url());

  await first.getByLabel("Descrição").fill("Descrição revisada pela primeira pessoa.");
  await first.getByRole("button", { name: "Salvar alterações" }).click();
  await expect(first.getByRole("status")).toContainText("Produto salvo");

  await second.getByLabel("Descrição").fill("Descrição da segunda pessoa.");
  await second.getByRole("button", { name: "Salvar alterações" }).click();
  await expect(
    second.getByRole("alert").filter({ hasText: "Outra pessoa alterou este produto" }),
  ).toBeVisible();
  await expect(second.getByLabel("Descrição")).toHaveValue("Descrição da segunda pessoa."); // o que ela digitou fica
  await context.close();
});

test("cadastro recusa SKU repetido e cria categoria", async ({ page }) => {
  await signIn(page, "admin@exemplo.com", "/admin/produtos");
  await page.getByRole("link", { name: "Novo produto" }).click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Novo produto");
  await page.getByLabel("Nome").fill("Fone repetido");
  await page.getByLabel("SKU").fill("SKU-0");
  await page.getByLabel("Categoria").selectOption({ label: "Fones de ouvido" });
  await page.getByLabel("Preço de tabela").fill("99,90");
  await page.getByRole("button", { name: "Cadastrar produto" }).click();
  await expect(
    page.getByRole("alert").filter({ hasText: "Já existe um produto com este SKU." }),
  ).toBeVisible();
  await expect(page.getByLabel("SKU")).toHaveAttribute("aria-invalid", "true");

  await page
    .getByRole("navigation", { name: "Seções do painel" })
    .getByRole("link", { name: "Categorias" })
    .click();
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Categorias");
  const name = `Caixas de som ${Date.now() % 100000}`;
  await page.getByLabel("Nome").fill(name);
  await page.getByLabel("Dentro de").selectOption({ label: "Eletrônicos" });
  await page.getByRole("button", { name: "Criar categoria" }).click();
  await expect(page.getByRole("status")).toHaveText(`Categoria “${name}” criada.`);
  await expect(page.getByRole("link", { name })).toHaveAttribute(
    "href",
    `/categoria/${name.toLowerCase().replace(/ /g, "-")}`,
  );
});

test("produto só fica à venda com o código na Montink, e a categoria volta sozinha (#483)", async ({
  page,
}) => {
  await signIn(page, "admin@exemplo.com", "/admin/categorias");
  const category = `Canecas ${Date.now() % 100000}`;
  const slug = category.toLowerCase().replace(/ /g, "-");
  await page.getByLabel("Nome").fill(category);
  await page.getByRole("button", { name: "Criar categoria" }).click();
  await expect(page.getByRole("status")).toHaveText(`Categoria “${category}” criada.`);
  await expect(
    page
      .getByRole("listitem")
      .filter({ hasText: category })
      .getByText("fora da loja: nenhum produto à venda"),
  ).toBeVisible();
  expect((await page.goto(`/categoria/${slug}`))?.status(), "vazia, a categoria não aparece na loja").toBe(
    404,
  );

  await page.goto("/admin/produtos/novo");
  await page.getByLabel("Nome").fill("Caneca Tio do Pavê");
  await page.getByLabel("Marca").fill("Embrulhado");
  await page.getByLabel("SKU").fill(`CAN-${Date.now() % 100000}`);
  await page.getByLabel("Categoria").selectOption({ label: `${category} (geral)` });
  await page.getByLabel("Preço de tabela").fill("49,90");
  await page.getByLabel("Cores").fill("Branco");
  await page.getByRole("radio", { name: /À venda/ }).check();
  await page.getByRole("button", { name: "Cadastrar produto" }).click();
  const code = page.getByRole("textbox", { name: "Código na Montink" });
  await expect(code).toHaveAttribute("aria-invalid", "true");
  await expect(
    page.getByText("para pôr à venda, informe o código do produto no fornecedor (Montink)"),
  ).toBeVisible();

  // a cor que não está nas cores do produto o painel recusa antes de mandar
  await code.fill("Branco: 6168525, Vermelho: 6170123");
  await page.getByRole("button", { name: "Cadastrar produto" }).click();
  await expect(page.getByText("A cor “Vermelho” não está nas cores do produto.")).toBeVisible();

  await page.getByRole("textbox", { name: "Código na Montink" }).fill("6168525");
  await page.getByRole("button", { name: "Cadastrar produto" }).click();
  await expect(page.getByRole("status")).toHaveText("Produto salvo. A vitrine já mostra a versão nova.");
  await expect(page.getByRole("textbox", { name: "Código na Montink" })).toHaveValue("6168525");

  const res = await page.goto(`/categoria/${slug}`);
  expect(res?.status(), "com o primeiro produto à venda, a categoria volta").toBe(200);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText(category);
});
