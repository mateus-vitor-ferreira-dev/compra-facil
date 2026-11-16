import type { NextConfig } from "next";

const config: NextConfig = {
  // o indicador do modo dev fica à direita: à esquerda, cobria o pé da coluna do painel (tema, sair)
  devIndicators: { position: "bottom-right" },
  reactStrictMode: true,
  poweredByHeader: false,
  // a planilha do fornecedor manual (#412) sobe por server action: o fulfillment aceita até 5 MB
  experimental: { serverActions: { bodySizeLimit: "6mb" } },
  // o front só fala com a API pelo servidor (BFF): nenhuma URL da API vai para o navegador
  env: {},
  async headers() {
    return [
      {
        source: "/(.*)",
        headers: [
          { key: "X-Content-Type-Options", value: "nosniff" },
          { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
          { key: "X-Frame-Options", value: "DENY" },
        ],
      },
    ];
  },
};

export default config;
