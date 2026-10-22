# Serviços .NET: template e BuildingBlocks

Os quatro serviços do time de compras (`orders`, `inventory`, `payments`, `notifications`) têm a mesma
forma. O que é igual entre eles fica aqui, em `libs/dotnet`, e não copiado em cada serviço.

| Projeto | O que é |
|---|---|
| `CompraFacil.BuildingBlocks` | Padrões HTTP, dados, mensageria (outbox, Kafka, inbox) e OpenTelemetry usados por todos (abaixo). |
| `CompraFacil.BuildingBlocks.Testing` | Postgres real via Testcontainers, o smoke test comum (herdado pelos serviços) e o `ContractAssert`: valida eventos e corpos HTTP contra `contracts/` (AsyncAPI e OpenAPI), seguindo os `$ref` entre arquivos. |
| `CompraFacil.BuildingBlocks.Tests` | Testes da própria lib. |

## O que o `AddServiceDefaults` / `UseServiceDefaults` dá a cada serviço

- **Controllers** com JSON em `snake_case` (propriedades e enums), como os contratos.
- **Erros em ProblemDetails** (RFC 9457) no formato do contrato: `type: about:blank`, título em
  português, `instance`, `request_id` e `trace_id` (W3C, para achar o trace no Grafana). Vale para
  exceções (500), rota inexistente (404), JSON malformado (400) e validação.
- **Erros de aplicação**: `NotFoundException` (404), `ConflictException` (409) e
  `DependencyUnavailableException` (503 com `Retry-After`) viram ProblemDetails sem código no controller.
- **Validação**: o caso de uso lança a `ValidationException` do FluentValidation e a resposta vira o
  `422` do contrato, com `errors: [{ field: "items[0].quantity", message }]` no caminho do JSON.
- **X-Request-ID**: reaproveitado (ou gerado), devolvido na resposta, no ProblemDetails e no escopo de
  log; uma linha de log JSON por requisição.
- **Health**: `/health` (processo vivo, sem olhar dependências) e `/ready` (checks com a tag `ready`,
  como o Postgres).
- **Docs do contrato**: `contracts/openapi/<serviço>.v1.yaml` em `/openapi` e o Swagger UI em `/docs`.
  O serviço implementa o contrato; não há OpenAPI gerado do código. Contrato ausente = falha na subida.
- **Postgres** (`AddPostgres<TContext>`): EF Core + Npgsql com nomes em `snake_case`, check no `/ready`
  e migrations aplicadas na subida (`Database:MigrateOnStart`, padrão `true`). Registra um `TimeProvider`
  truncado em microssegundos (precisão do `timestamptz`): a resposta de uma escrita bate com a leitura.
- **Rotas admin** (`AddAdminAuth(audience)` + `[Authorize(Policy = AdminAuth.Policy)]`): o mesmo JWT do
  catalog (HS256, `iss` compra-facil, `roles` com `admin`, `sub` e `exp`), com a audiência = serviço.
  Token local: `task token -- -aud inventory`. Segredo de dev barrado fora de Development, salvo
  `Auth__AllowDevSecret=true` (só no compose).
- **OpenTelemetry**: traces (HTTP de entrada e saída, Npgsql, Kafka) e métricas (ASP.NET, runtime,
  Npgsql). Exporta por OTLP quando `OTEL_EXPORTER_OTLP_ENDPOINT` está definido.

## Mensageria (`AddMessaging<TContext>`)

Mesmo desenho do catalog (Go), para os eventos de `contracts/asyncapi`:

```csharp
// Infrastructure/DependencyInjection.cs
builder.AddMessaging<OrdersDbContext>("orders", m => m
    .Produces("orders.order.v1")                                                  // tópicos de que é dono
    .Handle<PaymentApproved, ConfirmOrder>("payments.payment.v1", "PaymentApproved")); // o que consome

// num caso de uso: o evento vai no mesmo SaveChanges da escrita
outbox.Add("orders.order.v1", order.Id.ToString(), "OrderPlaced", new OrderPlaced(...));
await db.SaveChangesAsync(ct);
```

- **Outbox** (`IOutbox`): o evento entra no change tracker e é gravado na transação da escrita. O DbContext
  chama `modelBuilder.AddMessagingTables()` (tabelas `outbox` e `inbox`, migration em cada serviço).
- **Envelope** igual ao contrato: `event_id` (UUIDv7), `correlation_id` (o `X-Request-ID` da requisição ou
  o do evento que está sendo tratado), `causation_id` (o `event_id` que causou este), `producer`, `data`
  em `snake_case`. O `traceparent` vai nos headers do Kafka.
- **Relay** (`BackgroundService`): lê o outbox em ordem, publica com o producer idempotente
  (Confluent.Kafka, acks=all) e apaga o que o broker confirmou. Advisory lock: uma réplica publica por vez.
  Kafka fora: backoff e os eventos esperam; o `/ready` não depende do Kafka. Cria os tópicos do serviço com
  a configuração do contrato (3 partições, 7 dias).
- **Consumer** (group = nome do serviço): `IHandleEvent<TData>` roda na mesma transação que grava o
  `event_id` no **inbox**; o offset só é confirmado depois. Evento repetido é ignorado, `event_type` sem
  handler é pulado, mensagem que não é envelope (ou cujo `data` não segue o contrato: campo obrigatório
  ausente) é registrada como erro e pulada, em vez de travar a partição. Handler que falha segura a
  partição e tenta de novo com backoff (retries limitados e dead letter topic: #80).
- Configuração na seção `Kafka` (`Kafka:BootstrapServers`, `PollInterval`, `PublishTimeout`, `MaxBackoff`,
  `BatchSize`, `Enabled`). No compose: `Kafka__BootstrapServers=redpanda:9092`.
- Entrega **at-least-once** de ponta a ponta: o inbox é o que torna o efeito exatamente uma vez.

Nova entidade ou tabela: `dotnet tool run dotnet-ef migrations add <Nome> --project
services/<s>/src/<...>.Infrastructure --startup-project services/<s>/src/<...>.Api --output-dir Migrations`.

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
