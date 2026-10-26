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
});

export const env = schema.parse({
  API_URL: process.env.API_URL,
  SITE_URL: process.env.SITE_URL,
  NODE_ENV: process.env.NODE_ENV,
});
