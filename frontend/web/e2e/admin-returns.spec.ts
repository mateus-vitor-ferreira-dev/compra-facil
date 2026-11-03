import { expect, test, type Page } from "@playwright/test";

// o stub guarda a instrução registrada: o segundo teste muda o que o primeiro vê
test.describe.configure({ mode: "serial" });

async function signIn(page: Page) {
  await page.goto("/admin/devolucoes");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).toHaveURL(/\/admin\/devolucoes$/);
}

test("mostra as devoluções ao fornecedor, com o que pede ação primeiro", async ({ page }) => {
  await signIn(page);
  await expect(page.getByRole("link", { name: "Devoluções" })).toHaveAttribute("aria-current", "page");
  const headings = page.getByRole("heading", { level: 2 });
  await expect(headings.nth(0)).toContainText("Diga ao cliente o que fazer");
  await expect(headings.nth(1)).toContainText("Recusadas pelo fornecedor");

  const rejected = page.getByRole("region", { name: /^Recusadas pelo fornecedor/ });
  await expect(rejected).toHaveAccessibleName(/1 devolução$/);
  await expect(rejected).toContainText("Pedido 6C3E4AB2, Recusada pelo fornecedor");
  await expect(rejected).toContainText("Motivo da recusa: produto fora da política");
  await expect(rejected).toContainText("A SEFAZ rejeitou a nota de devolução: 267: sem referência");
  await expect(rejected.getByRole("link", { name: "dani@exemplo.com" })).toHaveAttribute(
    "href",
    "mailto:dani@exemplo.com",
  );

  const overdue = page.getByRole("region", { name: /^Sem resposta além do prazo/ });
  await expect(overdue).toContainText("O fornecedor não respondeu (3 tentativas)");
  await expect(overdue).toContainText("Prazo venceu há 2 dias");
  await overdue.getByText("Último erro").click();
  await expect(overdue).toContainText("fornecedor respondeu 503");

  const waiting = page.getByRole("region", { name: /^Esperando o fornecedor/ });
  await expect(waiting).toContainText("Aberta no fornecedor");
  await expect(waiting).toContainText("código RMA00000002");
  await expect(waiting).not.toContainText("Prazo venceu");
});

test("recusada na abertura: a equipe diz ao cliente o que fazer, e a devolução sai do topo (#281)", async ({
  page,
}) => {
  await signIn(page);
  const instruct = page.getByRole("region", { name: /^Diga ao cliente o que fazer/ });
  await expect(instruct).toContainText("Pedido 9D4E5BC3, Recusada na abertura");
  const choice = instruct.getByRole("group", {
    name: "O que o cliente do pedido 9D4E5BC3 faz com o produto?",
  });

  // o código e o endereço só aparecem no envio, e são obrigatórios nele
  await expect(instruct.getByLabel("Código da devolução")).toBeHidden();
  await choice.getByRole("radio", { name: /Manda para a loja/ }).check();
  await instruct.getByLabel("Endereço para o envio").fill("Rua da Loja, 10 - Lavras/MG");
  await instruct.getByRole("button", { name: "Mandar ao cliente" }).click();
  await expect(instruct.getByRole("alert")).toContainText("Confira os campos marcados.");
  await expect(instruct.getByLabel("Código da devolução")).toBeFocused();
  await expect(instruct.getByLabel("Endereço para o envio")).toHaveValue("Rua da Loja, 10 - Lavras/MG");

  await instruct.getByLabel("Código da devolução").fill("LOJA-123");
  await instruct.getByRole("button", { name: "Mandar ao cliente" }).click();
  await expect(page.getByRole("status")).toContainText(
    "Instrução do pedido 9D4E5BC3 registrada. O cliente recebe por e-mail.",
  );
  await expect(page.getByRole("region", { name: /^Diga ao cliente o que fazer/ })).toHaveCount(0);
  const rejected = page.getByRole("region", { name: /^Recusadas pelo fornecedor/ });
  await expect(rejected).toContainText("Combinado: o cliente manda para Rua da Loja, 10 - Lavras/MG");
});
