import "server-only";
import { NextResponse } from "next/server";

/**
 * Defesa de CSRF das rotas do BFF que mudam estado, além do cookie SameSite=Lax: só aceitam JSON (um
 * formulário de outro site não consegue mandar application/json sem preflight de CORS) e, se houver
 * Origin, ela tem que ser a do próprio site.
 */
export function rejectCrossSite(request: Request): NextResponse | null {
  const type = request.headers.get("content-type") ?? "";
  if (!type.toLowerCase().startsWith("application/json")) {
    return problem(415, "Tipo de conteúdo não suportado", "envie application/json");
  }
  const origin = request.headers.get("origin");
  if (origin && origin !== new URL(request.url).origin) {
    return problem(403, "Origem não permitida", "requisição de outro site");
  }
  return null;
}

/** ProblemDetails (RFC 9457) no mesmo formato das APIs. */
export function problem(status: number, title: string, detail: string): NextResponse {
  return NextResponse.json(
    { type: "about:blank", title, status, detail },
    { status, headers: { "Content-Type": "application/problem+json" } },
  );
}
