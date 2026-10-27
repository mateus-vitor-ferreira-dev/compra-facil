import { NextResponse, type NextRequest } from "next/server";
import { gatewayHeaders } from "@/lib/api/gateway-headers";
import {
  GOOGLE_COOKIE,
  GOOGLE_COOKIE_MAX_AGE,
  GOOGLE_LINK_COOKIE,
  emailOf,
  encodeCookie,
  exchangeCode,
  readPending,
} from "@/lib/auth/google";
import { sessionCookies, type Tokens } from "@/lib/auth/session";
import { env } from "@/lib/env";

/**
 * A volta do Google: confere o state (o callback é deste navegador, deste login), troca o código pelo
 * ID token e o entrega ao identity com o nonce. Sessão aberta: cookies e de volta para onde a pessoa
 * estava. E-mail que já tem conta com senha: a tela de vínculo pede a senha.
 */
export async function GET(request: NextRequest): Promise<NextResponse> {
  const params = request.nextUrl.searchParams;
  const pending = readPending(request.cookies.get(GOOGLE_COOKIE)?.value);
  const to = (path: string) => NextResponse.redirect(new URL(path, request.url));
  const fail = (reason: string) => clearPending(to(`/entrar?erro=${reason}`));

  if (params.get("error")) return fail("google-cancelado"); // a pessoa desistiu na tela do Google
  const code = params.get("code");
  if (!pending || !code || params.get("state") !== pending.state) return fail("google");

  const idToken = await exchangeCode(code, pending.verifier);
  if (!idToken) return fail("google");

  let response: Response;
  try {
    response = await fetch(`${env.API_URL.replace(/\/$/, "")}/identity/v1/sessions/google`, {
      method: "POST",
      headers: { "Content-Type": "application/json", ...gatewayHeaders(request.headers) },
      body: JSON.stringify({ id_token: idToken, nonce: pending.nonce }),
      cache: "no-store",
    });
  } catch {
    return fail("google");
  }

  if (response.ok) {
    const redirect = clearPending(to(pending.back));
    for (const c of sessionCookies((await response.json()) as Tokens))
      redirect.cookies.set(c.name, c.value, c.options);
    return redirect;
  }
  if (response.status === 409) {
    const redirect = clearPending(to("/entrar/vincular"));
    redirect.cookies.set(
      GOOGLE_LINK_COOKIE,
      encodeCookie({ idToken, nonce: pending.nonce, email: emailOf(idToken), back: pending.back }),
      {
        httpOnly: true,
        secure: env.NODE_ENV === "production",
        sameSite: "lax",
        path: "/entrar",
        maxAge: GOOGLE_COOKIE_MAX_AGE,
      },
    );
    return redirect;
  }
  return fail(response.status === 429 ? "muitas-tentativas" : "google");
}

function clearPending(response: NextResponse): NextResponse {
  response.cookies.set(GOOGLE_COOKIE, "", { path: "/api/auth/google", maxAge: 0 });
  return response;
}
