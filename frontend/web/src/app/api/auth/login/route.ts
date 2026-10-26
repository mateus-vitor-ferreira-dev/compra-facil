import { NextResponse } from "next/server";
import { z } from "zod";
import { api } from "@/lib/api/client";
import { problem, rejectCrossSite } from "@/lib/auth/request";
import { sessionCookies } from "@/lib/auth/session";

const body = z.object({ email: z.string().max(254), password: z.string().max(128) });

/** Login pelo BFF: os tokens do identity viram cookies httpOnly; o navegador nunca os vê. */
export async function POST(request: Request): Promise<NextResponse> {
  const rejected = rejectCrossSite(request);
  if (rejected) return rejected;
  const parsed = body.safeParse(await request.json().catch(() => null));
  if (!parsed.success) return problem(400, "Requisição malformada", "envie email e password");

  const { data, error, response } = await api.identity().POST("/v1/sessions", { body: parsed.data });
  if (!data) {
    // 401, 403 (e-mail não confirmado), 423 (bloqueio), 429: o ProblemDetails do identity segue igual
    return NextResponse.json(error, {
      status: response.status,
      headers: {
        "Content-Type": "application/problem+json",
        ...(response.headers.get("retry-after")
          ? { "Retry-After": response.headers.get("retry-after")! }
          : {}),
      },
    });
  }
  const res = new NextResponse(null, { status: 204 });
  for (const c of sessionCookies(data)) res.cookies.set(c.name, c.value, c.options);
  return res;
}
