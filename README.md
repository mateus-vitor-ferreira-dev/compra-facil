# CompraFácil — do monolito modular ao microsserviço de Catálogo

Implementação da **Proposta B** do trabalho *Monolito x Microsserviços*: o Catálogo sai do monolito e vira um
microsserviço, implantado e escalado de forma independente. Pedidos, Estoque, Pagamentos e Notificações
continuam no monolito. A extração usa o padrão **Strangler Fig**, e o serviço novo convive com a
implementação antiga enquanto é validado.

> Enunciado: [`docs/Trabalho_MonolitoXMicrosservicos.pdf`](docs/Trabalho_MonolitoXMicrosservicos.pdf) ·
> Parecer: [`docs/parecer.md`](docs/parecer.md) · Stack e decisões: [`docs/stack.md`](docs/stack.md)

## Visão geral

```
                    ┌──────────────── Traefik (gateway / strangler) ────────────────┐
  cliente ─────────▶│  /catalog/*  ──weight──▶ catalog-service (Go)   ◀── mirror ──┐ │
                    │              └─────────▶ monolith /catalog (legado) ──────────┘ │
                    │  /orders, /payments ...──▶ monolith (.NET)                      │
                    └─────────────────────────────────────────────────────────────────┘
   monolith ──gRPC──▶ catalog-service          (validar itens/preço ao criar pedido)
   catalog-service ──Kafka (Redpanda)──▶ monolith   (catalog.product.v1 → read model local)
   tudo ──OTLP──▶ OpenTelemetry Collector ──▶ Grafana LGTM (Tempo · Loki · Prometheus)
```

## Estrutura

```
compra-facil/
├── docs/                    # parecer, arquitetura (C4), ADRs, runbooks
├── contracts/               # fonte da verdade dos contratos entre os times
│   ├── proto/catalog/v1/    #   gRPC interno (Buf: lint + breaking change)
│   ├── openapi/             #   REST público do catálogo
│   └── asyncapi/            #   eventos Kafka (catalog.product.v1)
├── monolith/                # .NET 10 — monolito modular
│   ├── src/
│   │   ├── CompraFacil.Host/          # Program.cs, composição dos módulos
│   │   ├── CompraFacil.SharedKernel/  # tipos comuns (Result, Money, eventos)
│   │   └── Modules/
│   │       ├── Catalog/               # LEGADO, desligado no corte
│   │       ├── Orders/
│   │       ├── Inventory/
│   │       ├── Payments/
│   │       └── Notifications/
│   │           # cada módulo: <Modulo>/ (internal) + <Modulo>.Contracts/ (única API pública)
│   └── tests/
│       ├── CompraFacil.ArchitectureTests/   # NetArchTest: módulo não acessa internals de outro
│       └── CompraFacil.IntegrationTests/    # Testcontainers (Postgres, Redpanda)
├── services/
│   └── catalog/             # Go — microsservício de Catálogo
│       ├── cmd/catalog/     #   main.go
│       ├── internal/
│       │   ├── product/     #   domínio + regras
│       │   ├── httpapi/     #   REST público (net/http)
│       │   ├── grpcapi/     #   gRPC interno
│       │   ├── storage/postgres/  # sqlc + pgx
│       │   ├── cache/       #   Redis (cache-aside)
│       │   ├── events/      #   outbox + producer Kafka (franz-go)
│       │   └── platform/    #   config, logger, OpenTelemetry
│       └── db/{migrations,queries}/   # goose + sqlc
├── gateway/traefik/         # roteamento ponderado + mirroring
├── infra/
│   ├── compose/             # ambiente local completo
│   ├── otel/                # config do Collector
│   ├── grafana/dashboards/  # RED por serviço, painel da campanha
│   └── k8s/helm/            # fase final: HPA só no catálogo
├── load-tests/k6/           # simulação de campanha promocional
├── scripts/                 # backfill de dados, seeds
└── .github/workflows/       # um pipeline por app (path filters) = deploy independente
```

## Rodando localmente

> Preenchido na tarefa *F0 · Docker Compose base*.
