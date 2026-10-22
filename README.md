# CompraFácil — microsserviços com .NET e Go

O e-commerce da CompraFácil, do trabalho *Monolito x Microsserviços*, construído direto em microsserviços.
Os serviços seguem a divisão dos **dois times do enunciado**:

| Time | Serviços | Stack |
|---|---|---|
| Catálogo e campanhas | `catalog` | **Go 1.26 + Gin + GORM** |
| Processamento de compras | `orders`, `inventory`, `payments`, `notifications` | **C# / ASP.NET Core 10 (Web API) + EF Core** |

O catálogo, que concentra o pico de leitura nas campanhas, escala e é implantado sozinho. O fluxo de
compra é uma **SAGA coreografada sobre Kafka**.

> Enunciado: [`docs/Trabalho_MonolitoXMicrosservicos.pdf`](docs/Trabalho_MonolitoXMicrosservicos.pdf) ·
> Parecer: [`docs/parecer.md`](docs/parecer.md) · Stack e decisões: [`docs/stack.md`](docs/stack.md)

## Visão geral

```
                         ┌─────────── Traefik (API gateway) ───────────┐
   cliente ─────────────▶│ /catalog  /orders  /payments  /inventory     │
                         └──────┬─────────┬──────────┬──────────┬───────┘
                                ▼         ▼          ▼          ▼
                          catalog(Go)  orders     payments   inventory      notifications
                           Redis│   ◀─gRPC─┘ (.NET)  (.NET)     (.NET)          (.NET)
                                │                │          │          │              ▲
                                └────────────────┴──── Kafka (Redpanda) ──────────────┘
      cada serviço: banco PostgreSQL próprio + outbox    ·    tudo ──OTLP──▶ Grafana LGTM
```

SAGA de compra: `OrderPlaced` → inventory reserva → `StockReserved` → payments cobra →
`PaymentApproved` → orders confirma. Uma falha dispara a compensação (`OrderCancelled` → inventory devolve
a reserva). Estados, sequências e garantias: [`docs/arquitetura/saga.md`](docs/arquitetura/saga.md).

## Estrutura

```
compra-facil/
├── docs/                          # parecer, arquitetura (C4), ADRs, runbooks
├── contracts/                     # contratos entre os times (fonte da verdade)
│   ├── proto/catalog/v1/          #   gRPC  (Buf: lint + breaking change)
│   ├── openapi/                   #   REST de cada serviço
│   └── asyncapi/                  #   eventos Kafka
├── services/
│   ├── catalog/                   # Go + Gin + GORM
│   │   ├── cmd/api/main.go
│   │   ├── internal/
│   │   │   ├── config/            #   Viper
│   │   │   ├── domain/            #   entidades e regras
│   │   │   ├── handler/           #   rotas Gin (≈ @RestController)
│   │   │   ├── service/           #   casos de uso (≈ @Service)
│   │   │   ├── repository/        #   GORM (≈ JpaRepository)
│   │   │   ├── grpcserver/        #   gRPC interno
│   │   │   ├── cache/             #   Redis
│   │   │   ├── events/            #   outbox + producer Kafka
│   │   │   └── telemetry/         #   OpenTelemetry
│   │   ├── migrations/            #   golang-migrate
│   │   └── test/                  #   integração (testcontainers-go)
│   ├── orders/                    # ASP.NET Core — mesmo layout nos 4 serviços .NET:
│   │   ├── src/
│   │   │   ├── CompraFacil.Orders.Api/            # Controllers, Program.cs
│   │   │   ├── CompraFacil.Orders.Application/    # casos de uso, DTOs, validação
│   │   │   ├── CompraFacil.Orders.Domain/         # entidades, regras, eventos
│   │   │   └── CompraFacil.Orders.Infrastructure/ # EF Core, Kafka, gRPC client
│   │   └── tests/{UnitTests,IntegrationTests}/
│   ├── inventory/
│   ├── payments/
│   └── notifications/
├── libs/dotnet/CompraFacil.BuildingBlocks/   # outbox, Kafka, OTel comuns aos serviços .NET
├── gateway/traefik/
├── infra/
│   ├── compose/                   # ambiente local completo
│   ├── otel/                      # OpenTelemetry Collector
│   ├── grafana/dashboards/
│   └── k8s/helm/                  # fase final: HPA no catálogo
├── load-tests/k6/                 # simulação de campanha promocional
├── scripts/                       # seeds
└── .github/workflows/             # um pipeline por serviço (path filters)
```

## Rodando localmente

Pré-requisito: Docker com Compose v2.

```bash
cp .env.example .env        # opcional: só se precisar trocar alguma porta
docker compose up -d --wait # sobe a infraestrutura e espera todos ficarem saudáveis
docker compose down         # para tudo (-v também apaga os dados)
```

| Componente | Acesso pelo host | Uso |
|---|---|---|
| PostgreSQL 17 | `localhost:55432` | um banco por serviço: `catalog`, `orders`, `inventory`, `payments`, `notifications` (usuário = nome do banco, senha `compra-facil`) |
| Redis 8 | `localhost:6379` (`REDIS_PORT`) | cache do catálogo |
| Redpanda (Kafka) | `localhost:19092` (dentro do compose: `redpanda:9092`) | eventos da SAGA e do catálogo |
| Redpanda Console | http://localhost:8080 | inspecionar tópicos e mensagens |
| Mailpit | SMTP `localhost:1025` · UI http://localhost:8025 | e-mails do `notifications` |

Os serviços da aplicação **só entram pelo gateway** (Traefik, `http://localhost`), cada um com o seu
prefixo. O Swagger de cada um abre pelo gateway:

| Serviço | Stack | Pelo gateway | Docs |
|---|---|---|---|
| `catalog` | Go + Gin | `http://localhost/catalog` | http://localhost/catalog/docs |
| `orders` | ASP.NET Core | `http://localhost/orders` | http://localhost/orders/docs |
| `inventory` | ASP.NET Core | `http://localhost/inventory` | http://localhost/inventory/docs |
| `payments` | ASP.NET Core | `http://localhost/payments` | http://localhost/payments/docs |
| `notifications` | ASP.NET Core | — (só consome eventos) | — |
| Dashboard do Traefik | | http://127.0.0.1:8090/dashboard/ | |

Para depurar um serviço direto (portas 8001–8005, gRPC 9001): `task up:debug`.

### Observabilidade (OpenTelemetry + Grafana, tudo open source)

Os 5 serviços e o gateway mandam **traces, métricas e logs** por OTLP para o OpenTelemetry Collector
(`infra/otel/collector.yaml`), que distribui para **Tempo** (traces), **Loki** (logs) e **Prometheus**
(métricas). O Collector também lê o lag dos consumers direto do Redpanda.

| | Acesso | |
|---|---|---|
| Grafana | http://127.0.0.1:3001 | entra sem login (só local); datasources e dashboards provisionados |
| Prometheus | http://127.0.0.1:9090 | |
| OTLP | `127.0.0.1:4317` (gRPC), `:4318` (HTTP) | para serviços rodando fora do compose |

- Um pedido vira **um trace só**: gateway → orders → catalog (gRPC) → Kafka → inventory/payments → orders →
  notifications. No Grafana: *Explore → Tempo*, e do trace para os logs dele (e do log para o trace, pelo
  `trace_id`).
- Métricas próprias: `compra_facil_orders_placed_total`, `compra_facil_orders_finished_total{status,reason}`
  (SAGAs concluídas × compensadas), `compra_facil_payments_charges_total`, `compra_facil_inventory_reservations_total`,
  `compra_facil_messaging_consumed_total{result}`, `catalog_cache_lookups_total{result}` (hit ratio) e
  `kafka_consumer_group_lag_sum_ratio{group}`; RED por serviço vem do OTel e das métricas que o Tempo gera dos traces.
- Laços de manutenção (relay do outbox, despachante de e-mails) e `/health`/`/ready` não geram traces.

### Para quem consome as APIs (front)

- Base: `http://localhost/<serviço>/v1/...` (ex.: `POST http://localhost/orders/v1/orders`). Contratos em
  [`contracts/openapi`](contracts/openapi).
- **CORS** liberado no gateway para as origens de `CORS_ALLOWED_ORIGINS` (padrão: `http://localhost:5173`
  e `http://localhost:3000`). Para o domínio do deploy: `CORS_ALLOWED_ORIGINS=https://meu-front.dev,...`.
  Headers que o front pode ler: `Location`, `ETag`, `X-Request-ID`, `Retry-After`, `X-Cache`.
- Erros sempre em `application/problem+json` (com `request_id`); o catálogo tem rate limit por IP
  (`429` com `Retry-After`).
- Compra: `POST /orders/v1/orders` (header `Idempotency-Key`) responde `202` com o pedido `pending`;
  consulte `GET /orders/v1/orders/{id}` até `confirmed` ou `cancelled` (`cancellation_reason`).
  Fluxo completo: [`docs/arquitetura/saga.md`](docs/arquitetura/saga.md).

Os serviços .NET seguem um template comum ([`libs/dotnet/README.md`](libs/dotnet/README.md)).

Os bancos são criados por `infra/compose/postgres/init-databases.sh` apenas na **primeira** subida do volume.
Para recriá-los: `task reset` (ou `docker compose down -v && docker compose up -d --wait`).

### Comandos do dia a dia ([go-task](https://taskfile.dev))

```bash
go install github.com/go-task/task/v3/cmd/task@latest   # uma vez
task            # lista tudo
task up         # / task down / task reset / task logs -- <serviço>
task test       # go test + dotnet test em todos os serviços
task lint       # golangci-lint, dotnet format --verify-no-changes, buf lint
task seed       # popula o catálogo
```

`test` e `lint` descobrem sozinhos os módulos Go (`go.mod`) e as solutions .NET em `services/` e `libs/`.
