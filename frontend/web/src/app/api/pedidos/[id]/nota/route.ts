import { z } from "zod";
import { accessToken } from "@/lib/auth/current";
import { gatewayHeaders } from "@/lib/api/gateway-headers";
import { problem } from "@/lib/auth/request";
import { env } from "@/lib/env";

type Context = { params: Promise<{ id: string }> };

/**
 * O DANFE da nota fiscal do pedido (#221), para o cliente baixar: o BFF pede ao fulfillment
 * (`/fulfillment/v1/me/invoices/{id}/danfe`) com o token do cookie e repassa o PDF. A nota de outro
 * cliente, ou a que ainda não saiu, dá 404 lá.
 */
export async function GET(request: Request, { params }: Context): Promise<Response> {
  const { id } = await params;
  if (!z.uuid().safeParse(id).success) return problem(404, "Recurso não encontrado", "pedido inexistente");
  const token = await accessToken();
  if (!token) return problem(401, "Não autenticado", "entre na conta para baixar a nota");

  let upstream: Response;
  try {
    upstream = await fetch(`${env.API_URL.replace(/\/$/, "")}/fulfillment/v1/me/invoices/${id}/danfe`, {
      headers: {
        ...gatewayHeaders(request.headers),
        Authorization: `Bearer ${token}`,
        Accept: "application/pdf",
      },
      cache: "no-store",
    });
  } catch {
    return problem(503, "Serviço indisponível", "o serviço da nota fiscal não respondeu");
  }
  if (!upstream.ok || !upstream.body) {
    return new Response(upstream.body, {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/problem+json" },
    });
  }
  return new Response(upstream.body, {
    headers: {
      "Content-Type": "application/pdf",
      "Content-Disposition":
        upstream.headers.get("content-disposition") ?? `attachment; filename="nfe-${id}.pdf"`,
      "Cache-Control": "private, no-store",
    },
  });
}
