# Serviços .NET: template e BuildingBlocks

Os quatro serviços do time de compras (`orders`, `inventory`, `payments`, `notifications`) têm a mesma
forma. O que é igual entre eles fica aqui, em `libs/dotnet`, e não copiado em cada serviço.

| Projeto | O que é |
|---|---|
| `CompraFacil.BuildingBlocks` | Padrões HTTP e de dados usados por todos (abaixo). Mensageria (outbox, Kafka, inbox) entra na #73. |
| `CompraFacil.BuildingBlocks.Testing` | Postgres real via Testcontainers e o smoke test comum, herdado pelos serviços. |
| `CompraFacil.BuildingBlocks.Tests` | Testes da própria lib. |

## O que o `AddServiceDefaults` / `UseServiceDefaults` dá a cada serviço

- **Controllers** com JSON em `snake_case` (propriedades e enums), como os contratos.
- **Erros em ProblemDetails** (RFC 9457) no formato do contrato: `type: about:blank`, título em
  português, `instance`, `request_id` e `trace_id` (W3C, para achar o trace no Grafana). Vale para
  exceções (500), rota inexistente (404), JSON malformado (400) e validação.
- **Validação**: o caso de uso lança a `ValidationException` do FluentValidation e a resposta vira o
  `422` do contrato, com `errors: [{ field: "items[0].quantity", message }]` no caminho do JSON.
- **X-Request-ID**: reaproveitado (ou gerado), devolvido na resposta, no ProblemDetails e no escopo de
  log; uma linha de log JSON por requisição.
- **Health**: `/health` (processo vivo, sem olhar dependências) e `/ready` (checks com a tag `ready`,
  como o Postgres).
- **Docs do contrato**: `contracts/openapi/<serviço>.v1.yaml` em `/openapi` e o Swagger UI em `/docs`.
  O serviço implementa o contrato; não há OpenAPI gerado do código. Contrato ausente = falha na subida.
- **Postgres** (`AddPostgres<TContext>`): EF Core + Npgsql com nomes em `snake_case`, check no `/ready`
  e migrations aplicadas na subida (`Database:MigrateOnStart`, padrão `true`).

## Forma de um serviço

```
services/orders/
├── CompraFacil.Orders.slnx               # solution do serviço (inclui as libs)
├── src/
│   ├── CompraFacil.Orders.Api/            # Program.cs, Controllers, appsettings; linka o contrato
│   ├── CompraFacil.Orders.Application/    # casos de uso + validadores (FluentValidation)
│   ├── CompraFacil.Orders.Domain/         # entidades e regras, sem ASP.NET nem EF
│   └── CompraFacil.Orders.Infrastructure/ # DbContext, migrations, Kafka, clientes de outros serviços
└── tests/
    ├── CompraFacil.Orders.UnitTests/      # (criado com a primeira regra de domínio)
    └── CompraFacil.Orders.IntegrationTests/
```

Dependências entre camadas: `Api → Application → Domain` e `Api → Infrastructure → Application`.
O `Program.cs` tem ~15 linhas: `AddServiceDefaults`, `AddApplication`, `AddInfrastructure`,
`UseServiceDefaults`, `MapControllers`, `MigrateDatabaseAsync`.

## Build e configuração comuns (raiz do repositório)

- `Directory.Build.props`: `net10.0`, nullable, avisos como erro, análise `latest-recommended`, estilo do
  `.editorconfig` no build, `packages.lock.json` (restore travado no CI).
- `Directory.Packages.props`: **todas** as versões de pacotes NuGet (Central Package Management).
- `global.json`: SDK 10.0.1xx ou mais novo e testes no Microsoft.Testing.Platform (xUnit v3).
- `services/dotnet.Dockerfile`: um Dockerfile para os quatro (o serviço vem por build arg), com runtime
  `aspnet:*-chiseled` (sem shell, não-root). O `HEALTHCHECK` chama `/app/service healthcheck`, que
  consulta o próprio `/ready`.

## Comandos

```bash
dotnet test --solution services/orders/CompraFacil.Orders.slnx   # ou: task test:dotnet
dotnet format services/orders/CompraFacil.Orders.slnx            # ou: task fmt / task lint:dotnet
dotnet run --project services/orders/src/CompraFacil.Orders.Api  # porta 8002, banco do compose
```

Os testes de integração sobem um Postgres com Testcontainers: precisam do Docker.
