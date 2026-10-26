import "server-only";
import { cookies } from "next/headers";
import { api, type IdentitySchemas } from "@/lib/api/client";
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
