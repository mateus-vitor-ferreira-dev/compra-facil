import "server-only";
import { cookies } from "next/headers";
import { notFound, redirect } from "next/navigation";
import { api, type IdentitySchemas } from "@/lib/api/client";
import { loginHref } from "./return-to";
import { ACCESS_COOKIE } from "./session";

export type Account = IdentitySchemas["schemas"]["Account"];

/** O access token do cliente logado (o proxy já o renovou se precisava), para chamar as APIs. */
export async function accessToken(): Promise<string | undefined> {
  return (await cookies()).get(ACCESS_COOKIE)?.value;
}

/** A conta logada (GET /identity/v1/me), ou nula sem sessão. */
export async function currentAccount(): Promise<Account | null> {
  const token = await accessToken();
  if (!token) return null;
  const { data } = await api.identity({ token }).GET("/v1/me", { cache: "no-store" });
  return data ?? null;
}

/** Para as páginas da conta: sem sessão, manda para o login e traz de volta para `path` depois. */
export async function requireAccount(path: string): Promise<{ account: Account; token: string }> {
  const token = await accessToken();
  const account = token ? await currentAccount() : null;
  if (!token || !account) redirect(loginHref(path));
  return { account, token };
}

/**
 * Para o painel (#154): sem sessão, o login; com sessão sem o papel `admin`, 404 (a área não se
 * anuncia para quem não é da equipe). As APIs conferem o papel de novo pelo token.
 */
export async function requireAdmin(path: string): Promise<{ account: Account; token: string }> {
  const session = await requireAccount(path);
  if (!session.account.roles.includes("admin")) notFound();
  return session;
}
