# Contratos gRPC (Protobuf + Buf)

Comunicação síncrona interna. Hoje há um só: **orders → catalog** para validar itens e congelar preços
na criação do pedido ([`catalog/v1/catalog.proto`](catalog/v1/catalog.proto)).

| RPC | Uso |
|---|---|
| `GetProduct` | um produto, qualquer status (NOT_FOUND se removido) |
| `BatchGetProducts` | até 100 produtos; os desconhecidos vêm em `not_found_ids` |
| `ValidateItems` | confere os itens do pedido e devolve preço efetivo (com campanha), `line_total` e `total`; problemas por item (`ITEM_STATUS_*`) não são erro gRPC |

## Comandos

```bash
task lint:proto                    # buf lint (STANDARD) + buf format
task proto:breaking                # buf breaking (FILE) contra main; BASE=develop para comparar com a develop
task proto:generate                # gera Go em services/catalog/gen (versionado)
```

O lado C# (orders) não usa `buf generate`: o `Grpc.Tools` compila o `.proto` no build do .NET, com o
namespace `CompraFacil.Contracts.Catalog.V1`.

## Evoluindo

- Compatível: campo novo com número novo, RPC novo, valor de enum novo.
- **Quebra** (barrada pelo `buf breaking`): remover/renomear campo, trocar tipo ou número, remover RPC.
  Para isso, crie `catalog/v2` e mantenha a v1 até o orders migrar. Ao remover um campo, reserve o número
  (`reserved 7;`).
