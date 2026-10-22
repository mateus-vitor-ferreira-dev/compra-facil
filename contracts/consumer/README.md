# Contratos consumer-driven

O consumidor escreve o que espera do provedor; o provedor verifica no pipeline dele, antes de publicar.
É a ideia do Pact sem a ferramenta, porque o PactNet ainda não suporta gRPC (ADR 0004).

| Contrato | Consumidor (escreve) | Provedor (verifica) |
|---|---|---|
| [`orders-catalog.json`](orders-catalog.json) — `ValidateItems` | `services/orders/tests/.../CatalogContractTests.cs` | `services/catalog/test/contract_orders_test.go` |

**Não edite o JSON à mão.** Ele é gerado das interações declaradas no teste do orders:

```bash
cd services/orders && UPDATE_CONTRACTS=1 dotnet test   # regrava o arquivo
```

O teste do orders também roda o código real dele (DI, Polly, cliente gRPC) contra um catalog falso que
responde o que o contrato promete e confere se o pedido feito é o do contrato. O do catalog cria o estado de
cada interação num Postgres real e chama o `ValidateItems` pelo servidor gRPC real.

Formato: `given` (produtos que o provedor precisa ter), `request` e `response`, com referências (`p1`,
`fantasma`) no lugar dos ids. Na resposta, só os campos listados são verificados; `"<string>"` casa com
qualquer texto não vazio, `"<uuid>"` com qualquer UUID, e `""` exige vazio.
