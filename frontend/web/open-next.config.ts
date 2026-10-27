// O build do OpenNext para o Cloudflare Workers (ADR 0006). As páginas da loja são dinâmicas (a vitrine
// e o estoque mudam o tempo todo), então o cache incremental padrão basta.
import { defineCloudflareConfig } from "@opennextjs/cloudflare";

export default defineCloudflareConfig({});
