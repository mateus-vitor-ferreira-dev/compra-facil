import { expect, test, type Page } from "@playwright/test";
import { STUB } from "./ports";
import { passTwoFactor } from "./two-factor";

// o modo manual (#412) vale só para o operador de @manual.exemplo.com na API falsa
async function login(page: Page, path: string) {
  await page.goto(path);
  await page.getByLabel("E-mail").fill("admin@manual.exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await passTwoFactor(page);
  await expect(page).toHaveURL(new RegExp(`${path}$`));
}

test("a planilha do fornecedor manual: aviso de planilha velha, erro por linha e o que mudou na loja", async ({
  page,
}) => {
  await login(page, "/admin/fornecedor");
  const sheet = page.getByRole("region", { name: "Planilha do fornecedor" });
  await expect(sheet).toContainText("12 produtos");
  await expect(sheet).toContainText("Faz mais de 3 dias");
  await expect(sheet.getByRole("link", { name: "Baixar o modelo" })).toHaveAttribute(
    "href",
    "/admin/fornecedor/modelo",
  );
  // sem API, não há feed para ler; o custo de exemplo da margem é em reais
  await expect(page.getByRole("button", { name: "Ler o feed agora" })).toHaveCount(0);
  await expect(page.getByLabel("Custo de exemplo (R$)").first()).toBeVisible();

  const file = page.getByLabel("Arquivo CSV");
  await file.setInputFiles({
    name: "fornecedor.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(
      "sku;nome;categoria;custo;estoque\nA-1;Mouse;mouses;10;5\nA-2;Teclado;teclados;doze;3\n",
    ),
  });
  await page.getByRole("button", { name: "Subir a planilha" }).click();
  const problems = page.getByRole("alert").filter({ hasText: "fornecedor.csv" });
  await expect(problems).toContainText("1 problema em fornecedor.csv. Nada mudou na loja");
  await expect(problems).toContainText("Linha 3");
  await expect(problems).toContainText('custo inválido: "doze"');

  await file.setInputFiles({
    name: "fornecedor.csv",
    mimeType: "text/csv",
    buffer: Buffer.from(
      "sku;nome;categoria;custo;estoque\nA-1;Mouse;mouses;10;5\nA-2;Teclado;teclados;12;3\n",
    ),
  });
  await page.getByRole("button", { name: "Subir a planilha" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Planilha com" })).toHaveText(
    "Planilha com 2 produtos: 1 novo, 1 alterado e 0 saíram da loja. A vitrine atualiza em segundos.",
  );
});

test("pedidos a fazer: a ficha de compra, o pedido feito, o rastreio, a entrega e o cancelamento confirmado", async ({
  page,
  request,
}) => {
  await request.post(`${STUB}/__manual/reset`);
  await login(page, "/admin/pedidos-a-fazer");
  await expect(page.getByRole("link", { name: "Pedidos a fazer" })).toHaveAttribute("aria-current", "page");

  // a ficha: pago há 26 h (passou de 24 h), com os dados para copiar na ordem do formulário do fornecedor
  const ticket = page.getByRole("listitem", { name: "Pedido 2D3C4E10" });
  await expect(ticket).toContainText("Pago há 26 h");
  await expect(ticket).toContainText("SKU FON-BT-01");
  await expect(ticket).toContainText(/CPF:\s?52998224725/);
  await expect(ticket).toContainText("Avenida Paulista, 1000, Apto 12, Bela Vista, São Paulo - SP, 01310100");
  await expect(ticket).toContainText(/Custo esperado R\$\s109,80/);
  await expect(ticket.getByRole("button", { name: "Copiar cpf" })).toBeVisible();

  // sem o número do fornecedor, o campo diz o que falta
  await ticket.getByRole("button", { name: "Registrar o pedido feito para o pedido 2D3C4E10" }).click();
  await expect(ticket.getByText("Copie o número do pedido do site do fornecedor.")).toBeVisible();
  await ticket.getByLabel("Nº do pedido no fornecedor").fill("PED-6001");
  await ticket.getByLabel("Pago nos produtos (R$)").fill("92,50");
  await ticket.getByRole("button", { name: "Registrar o pedido feito para o pedido 2D3C4E10" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Pedido 2D3C4E10 registrado. O cliente vê que ele foi para o fornecedor.",
  );
  await expect(page.getByRole("region", { name: /^A fazer no fornecedor/ })).toContainText(
    "Nenhum pedido esperando",
  );

  // agora ele espera o rastreio, ao lado do que já estava feito
  const toShip = page.getByRole("region", { name: /^Esperando o rastreio/ });
  await expect(toShip).toHaveAccessibleName(/2 pedidos$/);
  const mine = toShip.getByRole("listitem").filter({ hasText: "Pedido 2D3C4E10" });
  await mine.getByLabel("Código de rastreio").fill("BR111222333BR");
  await mine.getByLabel("Transportadora").fill("Correios");
  await mine.getByRole("button", { name: "Enviar o rastreio ao cliente do pedido 2D3C4E10" }).click();
  await expect(page.getByRole("status")).toHaveText("Rastreio do pedido 2D3C4E10 enviado ao cliente.");

  // feito no fornecedor, só cancela com a confirmação de que a equipe cancelou lá
  const other = page
    .getByRole("region", { name: /^Esperando o rastreio/ })
    .getByRole("listitem")
    .filter({ hasText: "Pedido 3E4D5F21" });
  await other.getByText("Cancelar o pedido").click();
  await other.getByLabel("Já cancelei o pedido PED-5501 no site do fornecedor.").check();
  await other.getByRole("button", { name: "Cancelar e estornar" }).click();
  await expect(page.getByRole("status")).toHaveText(
    "Pedido 3E4D5F21 cancelado: o cliente é estornado por inteiro.",
  );

  const transit = page.getByRole("region", { name: /^Em transporte/ });
  await expect(transit).toContainText("Pedido 2D3C4E10");
  await transit.getByRole("button", { name: "Marcar como entregue o pedido 4F5E6A32" }).click();
  await expect(page.getByRole("status")).toHaveText("Pedido 4F5E6A32 marcado como entregue.");
});

test("sem o modo manual, a página manda para os pedidos no fornecedor", async ({ page }) => {
  await page.goto("/admin/pedidos-a-fazer");
  await page.getByLabel("E-mail").fill("admin@exemplo.com");
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await passTwoFactor(page);
  await expect(page.getByText("O fornecedor não está no modo manual")).toBeVisible();
  await expect(page.getByRole("link", { name: "Ver os pedidos no fornecedor" })).toHaveAttribute(
    "href",
    "/admin/atrasos",
  );
});
