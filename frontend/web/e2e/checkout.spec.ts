import { expect, test, type Page } from "@playwright/test";

const STUB = "http://localhost:4010";
const TV = "/produto/0199f6a0-0000-7000-8000-000000000000";
const HEADPHONE = "0199f6a0-0000-7000-8000-000000000031"; // "modelo 29": só este teste muda o preço

async function addToCart(page: Page, path: string) {
  await page.goto(path);
  await page.locator("form[action] input[name=quantity]").waitFor();
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Adicionado ao carrinho" })).toBeVisible();
}

/** A entrega de quem compra pela primeira vez (a Ana já tem, do pedido anterior). */
async function fillDelivery(page: Page) {
  await page.getByLabel("Nome de quem recebe").fill("Bia Lima");
  await page.getByLabel("CPF").fill("52998224725");
  await page.getByLabel("Celular").fill("11987654321");
  await page.getByLabel("CEP").fill("01310100");
  await expect(page.getByLabel("Rua")).toHaveValue("Avenida Paulista");
  // achado o endereço, o foco vai para o número
  await expect(page.getByLabel("Número")).toBeFocused();
  await page.getByLabel("Número").fill("1000");
  // com o CEP, o frete é cotado e o mais barato vem marcado
  await expect(page.getByRole("radio", { name: /Econômico/ })).toBeChecked();
}

/** Paga com o cartão de teste; o titular decide o resultado no provedor (APRO aprova, FUND recusa). */
async function payByCard(page: Page, holder = "APRO") {
  await page.getByRole("radio", { name: /Cartão de crédito/ }).check();
  await page.getByLabel("Número do cartão").fill("4509 9535 6623 3704");
  await page.getByLabel("Nome impresso no cartão").fill(holder);
  await page.getByLabel("Validade").fill("11/30");
  await page.getByLabel("Código de segurança").fill("123");
}

async function signIn(page: Page, email: string) {
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);
}

test("compra no cartão do começo ao fim, com a SAGA aparecendo ao vivo", async ({ page }) => {
  await addToCart(page, TV);
  await page.goto("/carrinho");
  await page.getByRole("link", { name: "Continuar para o pagamento" }).click();

  // sem sessão, o checkout pede login e volta para ele
  await expect(page).toHaveURL(/\/entrar\?voltar=%2Fcheckout$/);
  await signIn(page, "ana@exemplo.com");
  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Revise e faça o pedido");
  // a Ana já comprou: o frete vem cotado com o CEP do último pedido, o mais barato marcado e somado
  const summary = page.getByRole("complementary", { name: "Resumo" });
  await expect(page.getByRole("radio", { name: /Econômico/ })).toBeChecked();
  await expect(summary).toContainText("Frete econômico");
  await expect(summary).toContainText("R$ 2.664,22");

  await payByCard(page);
  await expect(page.getByText("Visa", { exact: true })).toBeVisible();
  await page.getByLabel("Parcelas").selectOption({ label: "3× de R$ 888,07 sem juros" });
  await page.getByRole("button", { name: "Fazer o pedido de R$ 2.664,22" }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);
  await expect(page.getByText("Recebemos o seu pedido.")).toBeVisible();

  const steps = page.getByRole("list", { name: "Andamento do pedido" });
  // a cobrança aparece como etapa própria antes do fim
  await expect(steps.locator("[aria-current=step]")).toContainText("Cobrando o pagamento");
  await expect(page.getByRole("status")).toContainText("Pagamento aprovado", { timeout: 10_000 });
  await expect(steps).toContainText("Estoque reservado");
  await expect(steps).toContainText("Pagamento aprovado");
  // pago, o pedido segue para o fornecedor; o cartão só é cobrado quando ele aceitar
  await expect(steps.locator("[aria-current=step]")).toContainText("Enviando ao fornecedor");
  await expect(page.getByRole("status")).toContainText("O cartão só é cobrado quando ele aceitar");

  // o carrinho foi esvaziado
  await page.goto("/carrinho");
  await expect(page.getByText("Seu carrinho está vazio")).toBeVisible();
});

test("o mesmo formulário enviado duas vezes faz um pedido só", async ({ browser }) => {
  // sem JavaScript: nada desliga o botão, então quem garante é a Idempotency-Key
  const context = await browser.newContext({ javaScriptEnabled: false });
  const page = await context.newPage();
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await page.goto(TV);
  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await page.goto("/checkout");

  const form = page.locator("form").filter({ has: page.locator("input[name=idempotency_key]") });
  const fields = await form.evaluate((f: HTMLFormElement) => [...new FormData(f)] as [string, string][]);
  const action = new URL((await form.getAttribute("action")) || "/checkout", page.url()).toString();

  const submit = () =>
    context.request.post(action, { multipart: Object.fromEntries(fields), maxRedirects: 0 });
  const [first, second] = await Promise.all([submit(), submit()]);
  const third = await submit(); // depois do carrinho esvaziado: volta ao mesmo pedido

  const target = first.headers()["location"];
  expect(target).toMatch(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);
  expect(second.headers()["location"]).toBe(target);
  expect(third.headers()["location"]).toBe(target);
  // só os pedidos desta chave: outros testes em paralelo também fazem pedidos
  const key = fields.find(([name]) => name === "idempotency_key")?.[1];
  const stats = (await (await page.request.get(`${STUB}/__stats`)).json()) as {
    ordersByKey: Record<string, number>;
  };
  expect(stats.ordersByKey[String(key)]).toBe(1);
  await context.close();
});

test("catálogo fora: o erro diz quando tentar de novo e mostra o código para o suporte", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "instavel@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await fillDelivery(page);
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();

  const alert = page.getByRole("alert").filter({ hasText: "O pedido não foi feito" });
  await expect(alert).toContainText("Tente de novo em 7 segundos");
  await expect(alert).toContainText("Nada foi cobrado");
  await expect(alert.locator("code")).toHaveText("4bf92f3577b34da6a3ce929d0e0e4736");
  await expect(page).toHaveURL(/\/checkout$/);
});

test("preço que mudou no meio da compra volta ao carrinho com o aviso", async ({ page, request }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, `/produto/${HEADPHONE}`);
  await page.goto("/checkout");
  const original = (await (await request.get(`${STUB}/catalog/v1/products/${HEADPHONE}`)).json()) as {
    effective_price: { amount_cents: number };
  };

  // um preço novo a cada execução: o cache de dados do Next (em disco) pode guardar o da anterior
  const changed = 20_000 + (Date.now() % 9_000);
  const shown = new Intl.NumberFormat("pt-BR", { style: "currency", currency: "BRL" }).format(changed / 100);
  try {
    await request.post(`${STUB}/__price`, { data: { product_id: HEADPHONE, amount_cents: changed } });
    await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();

    await expect(page).toHaveURL(/\/carrinho\?conferir=1$/);
    await expect(
      page.getByRole("alert", { name: "O pedido não foi feito: algo mudou no carrinho" }),
    ).toBeVisible();
    await expect(page.getByText(`agora ${shown}`)).toContainText(/Ficou mais (caro|barato): era R\$/);
  } finally {
    await request.post(`${STUB}/__price`, {
      data: { product_id: HEADPHONE, amount_cents: original.effective_price.amount_cents },
    });
  }
});

test("PIX: o código aparece no pedido e, pago, o pedido segue", async ({ page, request }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await expect(page.getByRole("radio", { name: /PIX/ })).toBeChecked();
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);

  const panel = page.getByRole("region", { name: "Valor do PIX" });
  await expect(panel).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText("Pague o PIX")).toBeVisible();
  await expect(
    page.getByRole("list", { name: "Andamento do pedido" }).locator("[aria-current=step]"),
  ).toContainText("Esperando o PIX");
  await expect(panel.getByRole("textbox", { name: "Código PIX copia e cola" })).toHaveValue(/^000201/);
  await expect(panel.getByRole("img", { name: /QR code do PIX de R\$ 2\.664,22/ })).toBeVisible();

  // o banco confirma o pagamento: o painel sai e o pedido segue, sem recarregar
  const id = new URL(page.url()).pathname.split("/").pop();
  await request.post(`${STUB}/__pix/${id}/pay`);
  await expect(page.getByRole("status").first()).toContainText("Pagamento aprovado", { timeout: 10_000 });
  await expect(panel).toBeHidden();
});

test("PIX vencido: o painel avisa e o pedido é cancelado sem cobrar", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "pix-curto@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await fillDelivery(page);
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();

  await expect(page.getByRole("region", { name: "Valor do PIX" })).toBeVisible({ timeout: 10_000 });
  await expect(page.getByText("Pedido cancelado")).toBeVisible({ timeout: 15_000 });
  await expect(page.getByText("O PIX venceu sem pagamento. Nada foi cobrado.")).toBeVisible();
  await expect(page.getByRole("list", { name: "Andamento do pedido" })).toContainText("PIX vencido");
});

test("cartão: os erros ficam no campo, e o recusado pelo banco cancela o pedido", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");

  await page.getByRole("radio", { name: /Cartão de crédito/ }).check();
  await page.getByLabel("Número do cartão").fill("4509 9535 6623 3705");
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  const number = page.getByLabel("Número do cartão");
  await expect(number).toBeFocused();
  await expect(number).toHaveAccessibleDescription(/algum dígito está errado/);
  await expect(page.getByLabel("Validade")).toHaveAccessibleDescription(/MM\/AA/);
  await expect(page).toHaveURL(/\/checkout$/);

  await payByCard(page, "FUND");
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);
  await expect(page.getByText("O cartão foi recusado. Nada foi cobrado.")).toBeVisible({ timeout: 10_000 });
});

test("o número do cartão não vai para a loja: só o token do provedor", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await payByCard(page);

  const sent: string[] = [];
  page.on("request", (r) => {
    if (new URL(r.url()).origin === "http://localhost:3000" && r.method() === "POST")
      sent.push(r.postData() ?? "");
  });
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\//);
  expect(sent.join("\n")).not.toContain("4509");
  expect(sent.join("\n")).not.toMatch(/"123"|security_code/);
});

test("entrega: o CEP preenche o endereço, os erros ficam no campo e o pedido mostra onde entregar", async ({
  page,
}) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "bia@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");

  // primeira compra: só o nome da conta; o resto, a pessoa digita
  await page.getByLabel("CPF").fill("529.982.247-24");
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page.getByLabel("CPF")).toHaveAccessibleDescription(/não existe/);
  await expect(page).toHaveURL(/\/checkout$/);

  // CEP de cidade pequena: vem só a cidade e o estado
  await page.getByLabel("CEP").fill("37200-000");
  await expect(page.getByLabel("Cidade")).toHaveValue("Lavras");
  await expect(page.getByLabel("Estado")).toHaveValue("MG");
  await page.getByLabel("CEP").fill("99999999");
  await expect(page.getByText("Não achamos este CEP")).toBeVisible();

  await fillDelivery(page);
  await expect(page.getByLabel("Bairro")).toHaveValue("Bela Vista");
  await page.getByLabel("Complemento (opcional)").fill("Apto 12");
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);

  const delivery = page.getByRole("region", { name: "Entrega" });
  await expect(delivery).toContainText("Bia Lima");
  await expect(delivery).toContainText("Avenida Paulista, 1000, Apto 12");
  await expect(delivery).toContainText("CEP 01310-100");
});

test("quem já comprou recebe a entrega do último pedido preenchida", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await expect(page.getByText("Os dados do seu último pedido")).toBeVisible();
  await expect(page.getByLabel("CPF")).toHaveValue("529.982.247-25");
  await expect(page.getByLabel("CEP")).toHaveValue("01310-100");
  await expect(page.getByLabel("Estado")).toHaveValue("SP");
});

test("frete: trocar pelo expresso muda o total, e o pedido mostra o frete e a data prometida", async ({
  page,
}) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "ana@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");

  const express = page.getByRole("radio", { name: /Expresso/ });
  await expect(express).toBeVisible();
  await expect(page.getByRole("group", { name: "Escolha o frete" })).toContainText(/chega entre/);
  await express.check();
  const summary = page.getByRole("complementary", { name: "Resumo" });
  await expect(summary).toContainText("Frete expresso");
  await expect(summary).toContainText("R$ 2.704,72"); // 2.639,92 + 64,80
  await page.getByRole("button", { name: "Fazer o pedido de R$ 2.704,72" }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);

  await expect(page.getByRole("region", { name: "Entrega" })).toContainText(/Chega até \d+ de/);
  await expect(page.getByText("Frete expresso")).toBeVisible();
});

test("CEP sem frete avisa, e o pedido não sai sem frete escolhido", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "caio@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
  await page.getByLabel("CEP").fill("99999-999");
  await expect(page.getByText("Nenhuma transportadora atende este CEP.")).toBeVisible();
  await page.getByRole("button", { name: /^Fazer o pedido de/ }).click();
  await expect(page).toHaveURL(/\/checkout$/);
});

test("o produto e o carrinho cotam o frete pelo CEP, e lembram o CEP", async ({ page }) => {
  await page.goto(TV);
  const product = page.getByRole("region", { name: "Frete e prazo" });
  await product.getByLabel("CEP").fill("01310100");
  await product.getByRole("button", { name: "Calcular" }).click();
  await expect(product).toContainText("Econômico");
  await expect(product).toContainText("R$ 24,30");
  await expect(product).toContainText(/chega entre/);

  await page.getByRole("button", { name: "Adicionar ao carrinho" }).click();
  await expect(page.getByRole("status").filter({ hasText: "Adicionado ao carrinho" })).toBeVisible();
  await page.goto("/carrinho");
  const cart = page.getByRole("region", { name: "Frete e prazo" });
  await expect(cart.getByLabel("CEP")).toHaveValue("01310-100");
  await expect(cart).toContainText("Expresso"); // cotou sozinho com o CEP guardado
});
