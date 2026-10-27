import "server-only";
import { z } from "zod";

/**
 * Configuração do servidor (BFF). Nada daqui vai para o navegador: o front fala com a API só pelo
 * servidor do Next, que guarda os tokens.
 */
const schema = z.object({
  // gateway Traefik: http://localhost no compose, a URL pública da API em produção
  API_URL: z.url().default("http://localhost"),
  // endereço público da loja: links absolutos do sitemap, do JSON-LD e das metatags
  SITE_URL: z.url().default("http://localhost:3000"),
  NODE_ENV: z.enum(["development", "test", "production"]).default("development"),
  // segredo compartilhado com o gateway: com ele, o rate limit é pelo IP do comprador (#176)
  BFF_GATEWAY_KEY: z.string().min(16).optional(),
  // o cabeçalho em que a plataforma entrega o IP do cliente, sobrescrevendo o que vier de fora
  // (x-real-ip na Vercel). Sem ele, o BFF não repassa IP nenhum: um cabeçalho que o próprio cliente
  // pode mandar deixaria cada requisição escolher o próprio limite
  CLIENT_IP_HEADER: z.string().min(1).optional(),
});

export const env = schema.parse({
  API_URL: process.env.API_URL,
  SITE_URL: process.env.SITE_URL,
  NODE_ENV: process.env.NODE_ENV,
  BFF_GATEWAY_KEY: process.env.BFF_GATEWAY_KEY || undefined,
  CLIENT_IP_HEADER: process.env.CLIENT_IP_HEADER || undefined,
});
