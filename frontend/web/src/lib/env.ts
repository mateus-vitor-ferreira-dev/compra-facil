import "server-only";
import { z } from "zod";

/**
 * Configuração do servidor (BFF). Nada daqui vai para o navegador: o front fala com a API só pelo
 * servidor do Next, que guarda os tokens.
 */
const schema = z.object({
  // gateway Traefik: http://localhost no compose, a URL pública da API em produção
  API_URL: z.url().default("http://localhost"),
  NODE_ENV: z.enum(["development", "test", "production"]).default("development"),
});

export const env = schema.parse({ API_URL: process.env.API_URL, NODE_ENV: process.env.NODE_ENV });
