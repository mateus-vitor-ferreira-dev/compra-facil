import "server-only";
import { env } from "@/lib/env";

const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

/**
 * IPv4 ou IPv6, sem `node:net`: roda também no middleware, que no Cloudflare Workers é Edge. O IPv6
 * passa pelo parser de URL, que recusa o que não for um endereço válido.
 */
export function isIp(value: string): boolean {
  if (IPV4.test(value)) return true;
  if (!value.includes(":") || /[^0-9a-f:.]/i.test(value)) return false;
  try {
    return new URL(`http://[${value}]/`).hostname.length > 2;
  } catch {
    return false;
  }
}

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
  if (!ip || !isIp(ip)) return {};
  return { "X-Bff-Key": env.BFF_GATEWAY_KEY, "X-Client-IP": ip };
}
