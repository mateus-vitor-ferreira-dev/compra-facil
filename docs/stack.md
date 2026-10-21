# Stack e justificativas

Critério de escolha: cada peça resolve um ponto do cenário da CompraFácil **e** é tecnologia que eu ainda
não usei em projeto (Node/Express, Spring, NestJS, RabbitMQ, Kong e Prometheus+Grafana já estão no
portfólio).

| Camada | Escolha | Por que aqui | Novo pra mim |
|---|---|---|---|
| Monolito | **C# / .NET 10** (ASP.NET Core Minimal APIs) | Linguagem muito pedida no mercado; projetos `.csproj` e o modificador `internal` deixam a fronteira entre módulos explícita | ✅ |
| Persistência do monolito | **EF Core + PostgreSQL**, um *schema* por módulo | "Regras de acesso aos dados": cada módulo só enxerga o próprio schema | ✅ EF Core |
| Fronteiras | **NetArchTest** nos testes | Quebra o build se um módulo depender dos internals de outro | ✅ |
| Catálogo | **Go 1.26** (`net/http` da stdlib) | Binário pequeno, sobe rápido e aguenta muita leitura com pouca memória, que é o caso das campanhas | ✅ (só estudo) |
| Acesso a dados (Go) | **pgx + sqlc + goose** | SQL escrito à mão, com código tipado gerado; migrações versionadas | ✅ |
| Cache | **Redis** (cache-aside) | O pico de leitura do catálogo vai para o cache | — |
| Sync interno | **gRPC + Protobuf**, com **Buf** | O pedido valida itens e preço no catálogo; o Buf barra quebra de contrato no CI | ✅ |
| Async | **Kafka**, rodando **Redpanda** no dev | Mudanças de produto viram eventos que o monolito consome; dá para reprocessar o log | ✅ |
| Consistência | **Transactional Outbox** (nos dois lados) | Evita perder evento quando o banco grava e o publish falha | ✅ |
| Gateway / strangler | **Traefik v3** | Roteamento ponderado (canário) e *mirroring* (tráfego sombra) prontos | ✅ |
| Contratos | **OpenAPI** (REST) · **AsyncAPI** (eventos) · **Pact** (consumer-driven) | "Definição de contratos" da proposta, verificada no CI | ✅ Pact, AsyncAPI |
| Testes de integração | **Testcontainers** (.NET e Go) · xUnit · `testing` | Postgres e Redpanda reais nos testes | ✅ |
| Observabilidade | **OpenTelemetry** → Collector → **Grafana LGTM** (Tempo, Loki, Prometheus) | "Acompanhamento operacional": um trace passa do gateway ao monolito e ao catálogo | ✅ tracing distribuído |
| Carga | **k6** | Simula a campanha promocional e mede os critérios do parecer | ✅ |
| CI/CD | **GitHub Actions** com *path filters* + **GHCR** | Um pipeline por app: o time do catálogo faz deploy sem esperar o outro | parcial |
| Orquestração (fase final) | **k3d + Helm + HPA** | Mostra o catálogo escalando sozinho | ✅ Helm/HPA |

## Mapa cenário → decisão

| Aspecto do cenário (PDF) | Onde a stack responde |
|---|---|
| Consultas ao catálogo crescem muito mais nas campanhas | Go + Redis + HPA só no catálogo; k6 para medir |
| Dois grupos coordenando uma implantação conjunta | Pipelines separados; contratos versionados em `contracts/` |
| Módulos identificáveis, com dependências a reorganizar | Fase 1: monolito modular com NetArchTest antes de extrair |
| Testes e deploy confiáveis, pouca experiência em distribuídos | Strangler gradual, com canário e mirroring reversíveis; observabilidade antes do corte |
| Serviço coexiste com o legado durante a validação | Traefik weight/mirror + *parallel run* comparando respostas |

## Critérios observáveis (para o parecer)

1. **Escala independente**: p95 de latência e taxa de erro do catálogo durante o pico do k6, com o
   catálogo escalado sozinho, comparados ao mesmo teste no monolito escalado inteiro (CPU/memória gastas).
2. **Autonomia de entrega**: deploys do catálogo por semana e *lead time* sem coordenação com o outro
   time (vem do histórico do GitHub Actions).
3. **Saúde da integração**: taxa de divergência no parallel run, falhas na verificação Pact e *lag* do
   consumidor Kafka (painéis no Grafana).
