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
`PaymentApproved` → orders confirma. Uma falha dispara a compensação (`StockReleased`, `OrderCancelled`).

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
| Redis 8 | `localhost:6379` | cache do catálogo |
| Redpanda (Kafka) | `localhost:19092` (dentro do compose: `redpanda:9092`) | eventos da SAGA e do catálogo |
| Redpanda Console | http://localhost:8080 | inspecionar tópicos e mensagens |
| Mailpit | SMTP `localhost:1025` · UI http://localhost:8025 | e-mails do `notifications` |

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
