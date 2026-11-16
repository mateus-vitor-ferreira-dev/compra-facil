// O Lighthouse CI contra o build de produção e a API falsa (scripts/serve-with-stub.mjs). A porta do front vem de
// E2E_FRONT_PORT, que o CI sorteia no runner próprio (#415); na máquina de desenvolvimento, 3000.
const front = `http://localhost:${process.env.E2E_FRONT_PORT ?? 3000}`;

const config = {
  ci: {
    collect: {
      startServerCommand: "node scripts/serve-with-stub.mjs",
      startServerReadyPattern: "Ready in",
      url: [
        "http://localhost:3000/",
        "http://localhost:3000/ofertas",
        "http://localhost:3000/categoria/eletronicos",
        "http://localhost:3000/produto/0199f6a0-0000-7000-8000-000000000000",
        "http://localhost:3000/carrinho",
        "http://localhost:3000/entrar",
      ],
      numberOfRuns: 3,
    },
    assert: {
      assertMatrix: [
        {
          matchingUrlPattern: "^(?!.*/(carrinho|entrar)).*$",
          assertions: {
            "categories:performance": [
              "error",
              {
                minScore: 0.9,
              },
            ],
            "categories:accessibility": [
              "error",
              {
                minScore: 0.9,
              },
            ],
            "categories:seo": [
              "error",
              {
                minScore: 0.9,
              },
            ],
            "categories:best-practices": [
              "error",
              {
                minScore: 0.9,
              },
            ],
          },
        },
        {
          matchingUrlPattern: ".*/(carrinho|entrar)$",
          assertions: {
            "categories:performance": [
              "error",
              {
                minScore: 0.9,
              },
            ],
            "categories:accessibility": [
              "error",
              {
                minScore: 0.9,
              },
            ],
            "categories:best-practices": [
              "error",
              {
                minScore: 0.9,
              },
            ],
          },
        },
      ],
    },
    upload: {
      target: "filesystem",
      outputDir: ".lighthouseci",
    },
  },
};

config.ci.collect.url = [
  "/",
  "/ofertas",
  "/categoria/eletronicos",
  "/produto/0199f6a0-0000-7000-8000-000000000000",
  "/carrinho",
  "/entrar",
].map((path) => `${front}${path}`);

module.exports = config;
