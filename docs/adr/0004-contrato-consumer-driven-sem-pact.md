# ADR 0004 — Contrato consumer-driven orders → catalog sem Pact

- **Status:** aceito
- **Data:** 2026-10-22
- **Issue:** #82

## Contexto

A única chamada síncrona entre os times é o orders validando e precificando os itens do pedido no catalog,
por gRPC (`ValidateItems`). O schema já é protegido pelo Buf: uma mudança incompatível no `.proto` não passa
no CI. Mas o Buf não pega mudança de **comportamento** com o mesmo schema, como o arredondamento do desconto,
um status trocado ou um campo que deixa de vir. É justamente disso que o orders depende, inclusive para a
cópia local que usa quando o catalog cai (#81).

O plano era o Pact: o orders (consumidor) gera o contrato, o catalog (provedor) verifica, e o `can-i-deploy`
segura a publicação. Para gRPC, o Pact precisa do plugin de protobuf, e o **PactNet não suporta plugins**:
a RFC está aberta ([pact-net#492](https://github.com/pact-foundation/pact-net/issues/492)) e a versão
atual é a 5.0.1, de março de 2025. Sem isso, o lado do consumidor, que é .NET, não tem como escrever o contrato.

## Opções

**A. Pact por HTTP**, expondo o `ValidateItems` também em REST. Mudaria a arquitetura para caber na ferramenta.

**B. Pact só no lado Go**, com o contrato escrito à mão. Perderia o "consumer-driven": ninguém garantiria que
o orders depende do que está escrito.

**C. O mesmo fluxo do Pact, com código próprio.**

## Decisão

**Opção C.** O fluxo é o do Pact:

1. O **orders declara as interações** de que depende, em C# (`CatalogContractTests`). Para cada uma, o teste
   roda o código real do orders (DI, Polly, cliente gRPC) contra um catalog falso que responde o que o contrato
   promete e confere se o orders pediu o que o contrato diz. O arquivo `contracts/consumer/orders-catalog.json`
   é **gerado** dessas interações, e o teste falha se ele estiver desatualizado.
2. O **catalog verifica o arquivo** (`test/contract_orders_test.go`): cria o estado de cada interação num
   Postgres real e chama o servidor gRPC real.
3. **"can-i-deploy":** a verificação roda nos testes do pipeline do catalog, e o job da imagem depende deles.
   Num monorepo, isso equivale ao `can-i-deploy` sem Pact Broker: o contrato vigente está no mesmo commit.

Só os campos que o orders lê entram no contrato, e o catalog pode mandar mais sem quebrá-lo.

## Consequências

- Uma mudança de comportamento no catalog que o orders sente (por exemplo, arredondar o desconto para cima)
  quebra o CI do catalog antes de publicar. Isso foi conferido mudando o preço esperado no contrato.
- Mudar o que o orders espera exige regravar o arquivo (`UPDATE_CONTRACTS=1`), e o PR dispara os dois
  pipelines (o arquivo está nos filtros de caminho dos dois).
- Não há Pact Broker nem matriz de versões. Com repositórios separados, voltaria a fazer falta; aí valeria
  reavaliar o PactNet, se os plugins tiverem saído.
