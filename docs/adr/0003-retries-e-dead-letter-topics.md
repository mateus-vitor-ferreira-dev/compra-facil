# ADR 0003 — Retries em tópicos próprios e dead letter topic por serviço

- **Status:** aceito
- **Data:** 2026-10-22
- **Issue:** #80

## Contexto

Os consumidores .NET (`EventConsumer` em `backend/libs/dotnet/CompraFacil.BuildingBlocks`) já eram
idempotentes: o inbox guarda o `event_id` na mesma transação do efeito, então a reentrega não repete
nada. Faltava decidir o que fazer com uma mensagem cujo handler **falha**. Até aqui havia dois casos:

1. **Mensagem envenenada** (não é envelope, ou `data` fora do contrato): era registrada no log e pulada.
   Ela se perdia, e só o log contava que tinha existido.
2. **Handler falhando** (banco fora, bug, dado inesperado): a mensagem era tentada de novo para sempre,
   com backoff, **segurando a partição**. Um único pedido com problema parava todos os pedidos da mesma
   partição (um terço do tráfego, com 3 partições) até alguém intervir.

## Opções

**A. Retry bloqueante limitado + DLT.** Tenta N vezes no lugar e, falhando, manda para a DLT.
Simples e preserva a ordem. Mas uma falha que dura mais que as N tentativas (o banco reiniciando, um
serviço vizinho em deploy) já manda a mensagem para a DLT, e N alto volta a travar a partição.

**B. Tópicos de retry com atraso crescente + DLT (retry não bloqueante).** Depois de poucas tentativas
no lugar, a mensagem é copiada para `<serviço>.retry-1`, depois `-2`, `-3`, cada nível com um atraso
fixo e um consumer próprio. A partição de origem segue andando. Esgotados os níveis, vai para
`<serviço>.dlt`. Custo: a mensagem que foi para o retry pode ser processada **depois** de mensagens
que chegaram após ela com a mesma chave.

**C. Só DLT, sem retry.** Qualquer falha vai direto para a DLT. Falhas passageiras viram trabalho manual.

## Decisão

**Opção B**, com 3 tentativas no lugar antes do primeiro nível.

- **Tentativas no lugar** (`Kafka:InPlaceAttempts`, padrão 3, backoff a partir de `PollInterval`):
  resolvem a falha de segundos sem tirar a mensagem da ordem.
- **Níveis de retry** (`Kafka:RetryDelays`, padrão 5 s, 30 s e 2 min): um tópico e um consumer group
  por nível (`orders.retry-1`, ...). Como todo o nível tem o mesmo atraso, a mensagem da frente é sempre
  a próxima a vencer: o consumer espera por ela sem precisar reordenar. Cada atraso tem que ficar
  abaixo de 5 min (`max.poll.interval.ms`), senão o consumer sairia do group enquanto espera.
- **DLT** (`<serviço>.dlt`, retenção de 30 dias): o que falhou em todos os níveis e as **envenenadas**,
  que vão direto para lá, porque tentar de novo não as conserta.
- A mensagem encaminhada é a **mesma** (chave, valor, `traceparent`), com headers de diagnóstico:
  `x-original-topic`, `x-original-partition`, `x-original-offset`, `x-attempts`, `x-error`,
  `x-failed-at` e, nos níveis de retry, `x-retry-at`. Nos tópicos de retry, o handler e o inbox usam
  o **tópico de origem**: para o serviço, é o mesmo evento.
- Se o próprio encaminhamento falhar (Kafka fora), o consumer tenta até conseguir e só então confirma
  o offset. A mensagem nunca é perdida.
- Os tópicos de retry e a DLT são **do serviço consumidor** (não são contrato, não estão no AsyncAPI) e
  ele mesmo os cria na subida.

### Por que a perda de ordem é aceitável aqui

Os handlers já toleram eventos fora de ordem, porque a entrega é at-least-once e o rebalance também
reordena:

- **Cópias do catálogo** (orders): cada escrita compara a `version` e remoção vira tombstone. Um evento
  atrasado não desfaz um mais novo.
- **SAGA**: cada passo depende do anterior ter sido processado. O `StockReserved` só existe depois de
  o inventory tratar o `OrderPlaced`, e o `PaymentFailed` só existe depois do `StockReserved`. Não há
  como o `OrderCancelled` de um pedido chegar ao inventory antes do `OrderPlaced` dele ter sido tratado.
- **Produtos no inventory**: `ProductDeleted` cria o registro já descontinuado se ele não existe, e o
  `ProductCreated` não recria um registro existente.

Um handler novo que dependa de ordem estrita precisa de uma guarda própria (versão ou estado), como
esses.

## Reprocessamento

Cada serviço .NET expõe, com o token de operador do identity (`task token`):

- `GET /v1/admin/dead-letters?limit=50`: o que está pendente, com origem, tentativas e último erro;
- `POST /v1/admin/dead-letters/replay`: devolve as pendentes para `<serviço>.retry-1`.

"Pendente" é o que chegou à DLT depois do último reprocessamento. A posição é o consumer group
`<serviço>.dlt-replay`. Reprocessar algo que, nesse meio-tempo, já tinha sido tratado não repete o
efeito (inbox). O que falhar de novo volta para a DLT pelo caminho normal.

No ambiente local: `task dlt -- payments` e `task dlt:replay -- payments`. A DLT também aparece no
Redpanda Console, com os headers.

## Consequências

- Uma falha num pedido não para os outros. O custo é ter 4 tópicos a mais por serviço consumidor.
- `compra_facil.messaging.consumed` ganhou os resultados `retried` e `dead_lettered`. O dashboard
  **SAGA** mostra os dois, e o lag de cada nível aparece no painel de consumer groups.
- **Envenenada não é mais pulada em silêncio**: fica 30 dias na DLT.
- A SAGA continua sem timeout. Um pedido cujo evento parou na DLT fica `Pending` até o reprocessamento.
  O timeout da SAGA é uma evolução separada.
