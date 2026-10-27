import { NextResponse, type NextRequest } from "next/server";
import {
  GOOGLE_COOKIE,
  GOOGLE_COOKIE_MAX_AGE,
  encodeCookie,
  googleEnabled,
  startLogin,
} from "@/lib/auth/google";
import { safeReturnTo } from "@/lib/auth/return-to";
import { env } from "@/lib/env";

/** "Continuar com o Google": guarda state, nonce e verifier e leva o navegador ao Google. */
export function GET(request: NextRequest): NextResponse {
  if (!googleEnabled()) return NextResponse.redirect(new URL("/entrar", request.url));
  const back = safeReturnTo(request.nextUrl.searchParams.get("voltar"));
  const { pending, url } = startLogin(back);
  const response = NextResponse.redirect(url);
  response.cookies.set(GOOGLE_COOKIE, encodeCookie(pending), {
    httpOnly: true,
    secure: env.NODE_ENV === "production",
    // Lax: o cookie volta na navegação do Google para o callback (GET de outro site, de primeiro nível)
    sameSite: "lax",
    path: "/api/auth/google",
    maxAge: GOOGLE_COOKIE_MAX_AGE,
  });
  return response;
}
