import { expect, test } from "@playwright/test";

const STUB = "http://localhost:4010";

test("a página inicial lista os produtos que o BFF busca no catálogo", async ({ page }) => {
  await page.goto("/");

  const products = page.getByRole("list", { name: "Produtos" });
  await expect(products.getByRole("listitem")).toHaveCount(3);
  await expect(products).toContainText("Smart TV 55");
});

test("login guarda a sessão em cookies httpOnly, fora do alcance do JavaScript", async ({
  page,
  context,
}) => {
  const login = await page.request.post("/api/auth/login", {
    data: { email: "ana@exemplo.com", password: "senha-boa-123" },
  });
  expect(login.status()).toBe(204);

  const cookies = await context.cookies();
  for (const name of ["cf_access", "cf_refresh"]) {
    const cookie = cookies.find((c) => c.name === name);
    expect(cookie, name).toBeDefined();
    expect(cookie?.httpOnly).toBe(true);
    expect(cookie?.sameSite).toBe("Lax");
  }

  await page.goto("/conta");
  await expect(page.getByTestId("account-email")).toContainText("ana@exemplo.com");
  expect(await page.evaluate(() => document.cookie)).not.toContain("cf_");
});

test("senha errada é 401 em ProblemDetails e não cria sessão", async ({ page, context }) => {
  const login = await page.request.post("/api/auth/login", {
    data: { email: "ana@exemplo.com", password: "errada" },
  });

  expect(login.status()).toBe(401);
  expect(login.headers()["content-type"]).toContain("application/problem+json");
  expect(await context.cookies()).toHaveLength(0);
});

test("access token vencido: o proxy renova com o refresh token, uma vez, e a sessão continua", async ({
  page,
  request,
}) => {
  await page.request.post("/api/auth/login", {
    data: { email: "curto@exemplo.com", password: "senha-boa-123" },
  });
  const before = (await (await request.get(`${STUB}/__stats`)).json()).refresh;

  await page.waitForTimeout(1500); // o cookie do access vence em 1 s (expires_in 61 - 60 de folga)
  await page.goto("/conta");

  await expect(page.getByTestId("account-email")).toContainText("curto@exemplo.com");
  const after = (await (await request.get(`${STUB}/__stats`)).json()).refresh;
  expect(after - before).toBe(1);
});

test("logout revoga a sessão e apaga os cookies", async ({ page, context }) => {
  await page.request.post("/api/auth/login", {
    data: { email: "ana@exemplo.com", password: "senha-boa-123" },
  });

  const logout = await page.request.post("/api/auth/logout", { data: {} });

  expect(logout.status()).toBe(204);
  expect((await context.cookies()).filter((c) => c.name.startsWith("cf_"))).toHaveLength(0);
  await page.goto("/conta");
  await expect(page.getByText("Você não está logado.")).toBeVisible();
});

test("POST de outro site é recusado", async ({ page }) => {
  const res = await page.request.post("/api/auth/login", {
    headers: { Origin: "https://evil.example" },
    data: { email: "ana@exemplo.com", password: "senha-boa-123" },
  });

  expect(res.status()).toBe(403);
});
