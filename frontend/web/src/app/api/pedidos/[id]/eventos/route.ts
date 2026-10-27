import { z } from "zod";
import { accessToken } from "@/lib/auth/current";
import { gatewayHeaders } from "@/lib/api/gateway-headers";
import { problem } from "@/lib/auth/request";
import { env } from "@/lib/env";

type Context = { params: Promise<{ id: string }> };

/**
 * O pedido ao vivo para o navegador: o EventSource não manda Authorization, então o BFF abre o stream do
 * orders (`/orders/v1/orders/{id}/events`) com o token do cookie e repassa os bytes como chegam. O
 * Last-Event-ID da reconexão vai junto; o 204 (o cliente já tem o estado final) faz o EventSource parar.
 * O proxy renova o access token antes, como nas páginas.
 */
export async function GET(request: Request, { params }: Context): Promise<Response> {
  const { id } = await params;
  if (!z.uuid().safeParse(id).success) return problem(404, "Recurso não encontrado", "pedido inexistente");
  const token = await accessToken();
  if (!token) return problem(401, "Não autenticado", "entre na conta para acompanhar o pedido");

  const headers: Record<string, string> = {
    ...gatewayHeaders(request.headers),
    Authorization: `Bearer ${token}`,
    Accept: "text/event-stream",
  };
  const lastEventId = request.headers.get("last-event-id");
  if (lastEventId) headers["Last-Event-ID"] = lastEventId;

  let upstream: Response;
  try {
    upstream = await fetch(`${env.API_URL.replace(/\/$/, "")}/orders/v1/orders/${id}/events`, {
      headers,
      cache: "no-store",
      signal: request.signal, // o navegador fechou: fecha o stream do orders também
    });
  } catch {
    return problem(503, "Serviço indisponível", "o serviço de pedidos não respondeu");
  }

  if (upstream.status === 204) return new Response(null, { status: 204 });
  if (!upstream.ok || !upstream.body) {
    return new Response(upstream.body, {
      status: upstream.status,
      headers: { "Content-Type": upstream.headers.get("content-type") ?? "application/problem+json" },
    });
  }
  return new Response(upstream.body, {
    headers: {
      "Content-Type": "text/event-stream; charset=utf-8",
      "Cache-Control": "no-cache, no-transform",
      Connection: "keep-alive",
      "X-Accel-Buffering": "no",
    },
  });
}
