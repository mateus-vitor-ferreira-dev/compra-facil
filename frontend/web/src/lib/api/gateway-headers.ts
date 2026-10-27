import "server-only";
import { isIP } from "node:net";
import { env } from "@/lib/env";

/**
 * O que o BFF manda ao gateway para o rate limit valer por comprador, e não para a loja inteira somada
 * (#176): o IP de quem está comprando (X-Client-IP) e o segredo do BFF (X-Bff-Key), sem o qual o gateway
 * ignora o IP e limita pela origem. O IP vem só do cabeçalho que a plataforma garante
 * (`CLIENT_IP_HEADER`); sem ele configurado, ou com um valor que não é IP, não vai nada, e o limite fica
 * como antes.
 */
export function gatewayHeaders(incoming: Headers): Record<string, string> {
  if (!env.BFF_GATEWAY_KEY || !env.CLIENT_IP_HEADER) return {};
  // x-forwarded-for da plataforma: "cliente, proxy1, ..."; o primeiro é o cliente
  const ip = incoming.get(env.CLIENT_IP_HEADER)?.split(",")[0]?.trim();
  if (!ip || isIP(ip) === 0) return {};
  return { "X-Bff-Key": env.BFF_GATEWAY_KEY, "X-Client-IP": ip };
}
