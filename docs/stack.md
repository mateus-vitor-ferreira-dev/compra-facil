# Stack e justificativas

Critério de escolha: cada peça resolve um ponto do cenário da CompraFácil **e** é tecnologia que eu ainda
não usei em projeto (Node/Express, Spring, NestJS, RabbitMQ, Kong e Prometheus+Grafana já estão no
portfólio).

## Frameworks (o "Spring" de cada linguagem)

| Spring / Java | .NET (time de compras) | Go (time de catálogo) |
|---|---|---|
| Spring Boot | **ASP.NET Core 10** | **Gin** |
| `@RestController` | `[ApiController]` Controllers | `gin.RouterGroup` handlers |
| Spring Data JPA / Hibernate | **EF Core** | **GORM** |
| Flyway | EF Core Migrations | **golang-migrate** |
| Bean Validation | **FluentValidation** | `binding` do Gin (go-playground/validator) |
| `application.yml` | `appsettings.json` + Options | **Viper** |
| Resilience4j | **Polly** (`Microsoft.Extensions.Http.Resilience`) | `sony/gobreaker` |
| JUnit + Testcontainers | **xUnit + Testcontainers** | `testing` + **testify** + **testcontainers-go** |
| springdoc / Swagger | Swashbuckle / Scalar | **swaggo/swag** |

## Infraestrutura e integração

| Camada | Escolha | Por que | Novo pra mim |
|---|---|---|---|
| Banco | **PostgreSQL**, um banco por serviço | Cada serviço é dono dos próprios dados | — |
| Cache | **Redis** no catálogo | Absorve o pico de leitura das campanhas | — |
| Sync interno | **gRPC + Protobuf**, com **Buf** | Orders valida preço e itens no catálogo; o Buf barra quebra de contrato | ✅ |
| Async | **Kafka**, rodando **Redpanda** no dev (Confluent.Kafka / franz-go) | SAGA de compra e eventos de produto, com replay do log | ✅ |
| Consistência | **Transactional Outbox** + consumidores idempotentes | Evita perder ou duplicar evento | ✅ |
| Fluxo de compra | **SAGA coreografada** | Sem transação distribuída; a compensação reverte reserva e pedido | parcial |
| Gateway | **Traefik v3** | Roteamento, rate limit, canário por peso | ✅ |
| Contratos | **OpenAPI** · **AsyncAPI** · **Pact** | Acordo entre os times, verificado no CI | ✅ |
| Observabilidade | **OpenTelemetry** → Collector → **Grafana LGTM** (Tempo, Loki, Prometheus) | Um trace atravessa gateway → orders → catalog → Kafka → payments | ✅ tracing |
| Carga | **k6** | Simula a campanha e mede os critérios | ✅ |
| CI/CD | **GitHub Actions** com path filters + **GHCR** | Deploy independente por serviço | parcial |
| Orquestração (fase final) | **k3d + Helm + HPA** | O catálogo escala sozinho | ✅ Helm/HPA |

## Mapa cenário → decisão

| Aspecto do cenário (PDF) | Onde a arquitetura responde |
|---|---|
| Consultas ao catálogo crescem muito mais nas campanhas | Catálogo em Go + Redis + HPA só nele; k6 para medir |
| Dois grupos coordenando uma implantação conjunta | Serviços separados por time, um pipeline cada |
| Módulos identificáveis, com dependências a reorganizar | Um serviço por módulo, comunicação só por contratos |
| Pouca experiência com sistemas distribuídos | Outbox, idempotência, Polly e observabilidade desde o início |

## Critérios observáveis (para o parecer)

1. **Escala independente**: p95 e taxa de erro do catálogo no pico do k6, escalando só o catálogo,
   e o CPU/memória total gastos.
2. **Autonomia de entrega**: deploys por semana e *lead time* por serviço (histórico do GitHub Actions).
3. **Saúde da integração**: SAGAs concluídas e compensadas, *lag* dos consumidores Kafka e falhas do Pact.
