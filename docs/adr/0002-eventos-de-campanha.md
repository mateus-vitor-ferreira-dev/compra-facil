# ADR 0002 — Campanhas publicam o próprio estado; o preço efetivo é calculado na leitura

- **Status:** aceito
- **Data:** 2026-10-22
- **Issue:** #110

## Contexto

O preço **efetivo** de um produto muda em dois tipos de momento:

1. quando o admin altera algo: preço de tabela, desconto ou período da campanha, produtos da campanha,
   remoção da campanha;
2. quando o relógio passa por `starts_at` ou `ends_at` de uma campanha, **sem nenhuma escrita no banco**.

Até aqui só o preço de tabela gerava evento (`ProductPriceChanged`). Quem precisa do preço cobrado chama
o gRPC `ValidateItems`, que lê do banco. Mas o orders guarda uma cópia local do catálogo para validar
pedidos quando o gRPC está fora. Sem as campanhas, essa cópia cobraria o preço cheio durante uma
promoção, e o pedido do cliente que viu o preço com desconto seria recusado (`409`). Isso aconteceria
justamente no pico, que é quando o catálogo tem mais chance de estar degradado.

Uma campanha tem até 1000 produtos (`MaxCampaignItems`).

## Opções

**A. Um evento por produto afetado (`EffectivePriceChanged`) no início e no fim de cada campanha.**
Exige um agendador no catalog para publicar algo que não nasce de uma escrita. Por isso fica fora do
outbox: com várias réplicas no HPA, ou o evento sai duplicado, ou é preciso eleição de líder; se o
agendador cair na hora, o evento se perde. E publica até 1000 eventos no instante exato em que a
campanha começa, que é o pico de carga.

**B. Eventos de início/fim por campanha (`CampaignStarted` / `CampaignEnded`).**
Reduz o volume, mas mantém o agendador e os mesmos problemas de entrega. Não cobre alteração e remoção
de campanha já vigente, que continuariam precisando de outros eventos.

**C. A campanha publica o próprio estado a cada escrita do admin; o consumidor calcula o preço na leitura.**
`CampaignCreated` / `CampaignUpdated` / `CampaignDeleted` com período, desconto e a lista completa de
produtos. Cada evento sai pelo outbox, na mesma transação da escrita. O início e o fim viram dados (`starts_at`,
`ends_at`) que o consumidor compara com o relógio, como o próprio catálogo já faz no SQL.

## Decisão

Opção **C**.

- Tópico `catalog.campaign.v1`, chave `campaign_id`, contrato em `contracts/asyncapi/catalog.v1.yaml`.
- Toda alteração publica o estado completo (igual ao `ProductUpdated`). A troca de produtos também é
  `CampaignUpdated`, com a lista nova inteira, em vez de um evento só de produtos adicionados ou removidos.
- O cálculo do preço efetivo fica documentado no contrato (`CampaignSnapshot`) e é o mesmo do catálogo:
  vigente se `starts_at <= t < ends_at`; com mais de uma, vale a de maior desconto (empate: a que acaba
  antes); `floor(preço * (100 - desconto) / 100)`.
- A tabela `campaigns` ganha uma coluna `version`, que incrementa a cada alteração, inclusive na troca de
  produtos, para o consumidor descartar eventos fora de ordem. Ela entra junto com o outbox (#68).
- Consumidor hoje: o orders, no snapshot de fallback. O preço cobrado continua vindo do gRPC sempre que
  ele responde.

### Cache do catálogo

Na análise apareceu um problema do cache Redis (#67): o TTL era encurtado no **fim** da campanha, mas não
no **início**. Um produto que entrou no cache pouco antes da campanha começar continuava com preço cheio
por até 5 minutos (1 minuto nas listagens), enquanto o `ValidateItems`, que lê do banco, já devolvia o
preço com desconto. Corrigido junto com este ADR: o TTL de produtos, listagens e campanhas ativas também
não passa do início da próxima campanha (`CampaignRepository.NextStart`). O limite vale para todas as
entradas, porque uma entrada gravada antes do início não sabe que o produto vai entrar em promoção.
Se essa consulta falhar, a resposta não vai para o cache.

## Consequências

- (+) Nenhum agendador: tudo que é publicado nasce de uma escrita e passa pelo outbox, sem perda nem
  duplicação por réplica.
- (+) Nenhum pico de eventos no início da campanha; o volume acompanha as ações do admin.
- (+) Alterar ou remover uma campanha vigente funciona igual a qualquer outra alteração.
- (−) A regra do preço efetivo existe em dois lugares (Go no catálogo, C# no orders). Mitigação: a regra
  está escrita no contrato, e os testes do orders (#78) devem cobrir os mesmos casos de borda do catálogo
  (início inclusivo, fim exclusivo, arredondamento, duas campanhas).
- (−) O consumidor depende do próprio relógio. Uma diferença de segundos entre as máquinas só afeta
  pedidos feitos no instante exato da virada e, mesmo assim, apenas no modo de fallback.
- (−) Uma campanha com 1000 produtos gera um evento de ~40 KB. Fica bem abaixo do limite de 1 MB do Kafka.
- (−) Cada miss do cache faz uma consulta a mais (`min(starts_at)`, coberta pelo índice `campaigns_period_idx`).
