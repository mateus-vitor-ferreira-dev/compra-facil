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

async function signIn(page: Page, email: string) {
  await page.getByLabel("E-mail").fill(email);
  await page.getByLabel("Senha", { exact: true }).fill("senha-boa-123");
  await page.getByRole("button", { name: "Entrar" }).click();
  await expect(page).not.toHaveURL(/\/entrar/);
}

test("compra do começo ao fim, com a SAGA aparecendo ao vivo", async ({ page }) => {
  await addToCart(page, TV);
  await page.goto("/carrinho");
  await page.getByRole("link", { name: "Continuar para o pagamento" }).click();

  // sem sessão, o checkout pede login e volta para ele
  await expect(page).toHaveURL(/\/entrar\?voltar=%2Fcheckout$/);
  await signIn(page, "ana@exemplo.com");
  await expect(page).toHaveURL(/\/checkout$/);
  await expect(page.getByRole("heading", { level: 1 })).toHaveText("Revise e faça o pedido");
  await expect(page.getByRole("complementary", { name: "Resumo" })).toContainText("R$ 2.639,92");

  await page.getByRole("button", { name: "Fazer o pedido de R$ 2.639,92" }).click();
  await expect(page).toHaveURL(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);
  await expect(page.getByText("Recebemos o seu pedido.")).toBeVisible();

  const steps = page.getByRole("list", { name: "Andamento do pedido" });
  // a cobrança aparece como etapa própria antes do fim
  await expect(steps.locator("[aria-current=step]")).toContainText("Cobrando o pagamento");
  await expect(page.getByRole("status")).toContainText("Pedido confirmado", { timeout: 10_000 });
  await expect(steps).toContainText("Estoque reservado");
  await expect(steps).toContainText("Pagamento aprovado");
  await expect(steps.locator("[aria-current=step]")).toHaveCount(0);

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
  const before = (await (await page.request.get(`${STUB}/__stats`)).json()).ordersPlaced as number;

  const submit = () =>
    context.request.post(action, { multipart: Object.fromEntries(fields), maxRedirects: 0 });
  const [first, second] = await Promise.all([submit(), submit()]);
  const third = await submit(); // depois do carrinho esvaziado: volta ao mesmo pedido

  const target = first.headers()["location"];
  expect(target).toMatch(/\/conta\/pedidos\/[0-9a-f-]{36}\?novo=1$/);
  expect(second.headers()["location"]).toBe(target);
  expect(third.headers()["location"]).toBe(target);
  const after = (await (await page.request.get(`${STUB}/__stats`)).json()).ordersPlaced as number;
  expect(after - before).toBe(1);
  await context.close();
});

test("catálogo fora: o erro diz quando tentar de novo e mostra o código para o suporte", async ({ page }) => {
  await page.goto("/entrar?voltar=%2Fcarrinho");
  await signIn(page, "instavel@exemplo.com");
  await addToCart(page, TV);
  await page.goto("/checkout");
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
