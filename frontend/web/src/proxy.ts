import { NextResponse, type NextRequest } from "next/server";
import {
  ACCESS_COOKIE,
  REFRESH_COOKIE,
  clearedCookies,
  refreshSession,
  sessionCookies,
} from "@/lib/auth/session";

/**
 * Renova a sessão antes da página: sem access token (venceu) e com refresh token, troca pelo identity e
 * entrega os cookies novos ao navegador e à própria requisição, para os Server Components já lerem o
 * token novo. Refresh recusado (sessão derrubada ou vencida): apaga os cookies e segue sem sessão.
 */
export async function proxy(request: NextRequest): Promise<NextResponse> {
  const refresh = request.cookies.get(REFRESH_COOKIE)?.value;
  if (request.cookies.has(ACCESS_COOKIE) || !refresh) {
    return NextResponse.next();
  }

  const result = await refreshSession(refresh);
  const cookies = result.ok ? sessionCookies(result.tokens) : clearedCookies();
  for (const c of cookies) {
    if (c.value) request.cookies.set(c.name, c.value);
    else request.cookies.delete(c.name);
  }
  const response = NextResponse.next({ request: { headers: request.headers } });
  for (const c of cookies) response.cookies.set(c.name, c.value, c.options);
  return response;
}

export const config = {
  // páginas e ações; fora os arquivos estáticos e as rotas do BFF de autenticação (que cuidam da sessão)
  matcher: ["/((?!_next/static|_next/image|favicon.ico|robots.txt|sitemap.xml|api/auth).*)"],
};
