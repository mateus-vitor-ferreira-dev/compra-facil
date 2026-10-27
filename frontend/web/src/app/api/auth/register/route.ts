import { NextResponse } from "next/server";
import { z } from "zod";
import { api } from "@/lib/api/client";
import { problem, rejectCrossSite } from "@/lib/auth/request";
import { sessionCookies } from "@/lib/auth/session";

const body = z.object({
  email: z.string().max(254),
  password: z.string().max(128),
  name: z.string().max(120),
});

/**
 * Cadastro pelo BFF e, em seguida, o login (quando o identity não exige a confirmação do e-mail antes:
 * aí responde 201 sem sessão e o front pede para confirmar).
 */
export async function POST(request: Request): Promise<NextResponse> {
  const rejected = rejectCrossSite(request);
  if (rejected) return rejected;
  const parsed = body.safeParse(await request.json().catch(() => null));
  if (!parsed.success) return problem(400, "Requisição malformada", "envie email, password e name");

  const identity = api.identity();
  const created = await identity.POST("/v1/accounts", { body: parsed.data });
  if (!created.data) {
    return NextResponse.json(created.error, {
      status: created.response.status,
      headers: { "Content-Type": "application/problem+json" },
    });
  }
  const login = await identity.POST("/v1/sessions", {
    body: { email: parsed.data.email, password: parsed.data.password },
  });
  const res = NextResponse.json(created.data, { status: 201 });
  if (login.data) {
    for (const c of sessionCookies(login.data)) res.cookies.set(c.name, c.value, c.options);
  }
  return res;
}
