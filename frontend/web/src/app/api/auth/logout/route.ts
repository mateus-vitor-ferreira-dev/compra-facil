import { NextResponse } from "next/server";
import { cookies } from "next/headers";
import { api } from "@/lib/api/client";
import { rejectCrossSite } from "@/lib/auth/request";
import { REFRESH_COOKIE, clearedCookies } from "@/lib/auth/session";

/** Logout: revoga a sessão no identity (melhor esforço) e apaga os cookies. */
export async function POST(request: Request): Promise<NextResponse> {
  const rejected = rejectCrossSite(request);
  if (rejected) return rejected;
  const refresh = (await cookies()).get(REFRESH_COOKIE)?.value;
  if (refresh) {
    // o identity responde 204 mesmo para token desconhecido; falha de rede não impede o logout local
    await api
      .identity()
      .POST("/v1/sessions/revoke", { body: { refresh_token: refresh } })
      .catch(() => undefined);
  }
  const res = new NextResponse(null, { status: 204 });
  for (const c of clearedCookies()) res.cookies.set(c.name, c.value, c.options);
  return res;
}
