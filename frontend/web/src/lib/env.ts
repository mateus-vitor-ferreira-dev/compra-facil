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
  // (cf-connecting-ip na Cloudflare). Sem ele, o BFF não repassa IP nenhum: um cabeçalho que o próprio cliente
  // pode mandar deixaria cada requisição escolher o próprio limite
  CLIENT_IP_HEADER: z.string().min(1).optional(),
  // login com o Google (#180): o cliente OAuth do Google Cloud. Sem os dois, o botão não aparece
  GOOGLE_CLIENT_ID: z.string().min(1).optional(),
  GOOGLE_CLIENT_SECRET: z.string().min(1).optional(),
  // os endereços do Google; trocados só nos testes (o stub faz o papel do Google)
  GOOGLE_AUTH_URL: z.url().default("https://accounts.google.com/o/oauth2/v2/auth"),
  GOOGLE_TOKEN_URL: z.url().default("https://oauth2.googleapis.com/token"),
  // o provedor de pagamento (#379, a Efí): o navegador tokeniza o cartão direto nele, com o identificador de
  // conta, e os dados do cartão não passam pela loja. São os únicos valores daqui que a página recebe (não são
  // segredo). "simulator" em dev, no CI e na demonstração (o payment-sim em PAYMENT_PROVIDER_URL); "sandbox" é a
  // homologação da Efí e "production" a conta de verdade, pela biblioteca payment-token-efi
  PAYMENT_PROVIDER: z.enum(["simulator", "sandbox", "production"]).default("simulator"),
  PAYMENT_PROVIDER_URL: z.url().default("http://localhost:8092"),
  PAYMENT_PAYEE_CODE: z
    .string()
    .regex(/^[a-zA-Z0-9]{32}$/)
    .default("0123456789abcdef0123456789abcdef"),
  /** Loja de demonstração: mostra o aviso de que nada é cobrado nem entregue. */
  DEMO_MODE: z.stringbool().default(false),
  // o endereço do CEP no checkout (#221); trocado só nos testes (o stub faz o papel do ViaCEP)
  CEP_LOOKUP_URL: z.url().default("https://viacep.com.br/ws"),
  // só nos testes de navegador: a rota que liga e desliga a marca do deploy (#370). Em produção, quem liga é o CI
  MAINTENANCE_TEST_HOOKS: z.stringbool().default(false),
});

export const env = schema.parse({
  API_URL: process.env.API_URL,
  SITE_URL: process.env.SITE_URL,
  NODE_ENV: process.env.NODE_ENV,
  BFF_GATEWAY_KEY: process.env.BFF_GATEWAY_KEY || undefined,
  CLIENT_IP_HEADER: process.env.CLIENT_IP_HEADER || undefined,
  GOOGLE_CLIENT_ID: process.env.GOOGLE_CLIENT_ID || undefined,
  GOOGLE_CLIENT_SECRET: process.env.GOOGLE_CLIENT_SECRET || undefined,
  GOOGLE_AUTH_URL: process.env.GOOGLE_AUTH_URL || undefined,
  GOOGLE_TOKEN_URL: process.env.GOOGLE_TOKEN_URL || undefined,
  PAYMENT_PROVIDER: process.env.PAYMENT_PROVIDER || undefined,
  PAYMENT_PROVIDER_URL: process.env.PAYMENT_PROVIDER_URL || undefined,
  PAYMENT_PAYEE_CODE: process.env.PAYMENT_PAYEE_CODE || undefined,
  DEMO_MODE: process.env.DEMO_MODE || undefined,
  CEP_LOOKUP_URL: process.env.CEP_LOOKUP_URL || undefined,
  MAINTENANCE_TEST_HOOKS: process.env.MAINTENANCE_TEST_HOOKS || undefined,
});
