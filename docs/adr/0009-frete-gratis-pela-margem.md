# ADR 0009: Frete grátis pela margem, com o preço da loja buscado no catalog

Status: aceito (03/11/2026)

## Contexto

O frete era o preço que o fornecedor cotava, repassado ao comprador sem alteração (#222). A loja precisava oferecer frete grátis sem nunca sair no prejuízo (#323), e um limite fixo de valor de compra não garante isso: o mesmo valor pode vir de um produto com margem larga ou de um com margem quase nula, e o frete muda com o CEP e o peso.

A conta precisa de três números que estão em lugares diferentes: o custo do produto no fornecedor (fulfillment, no feed do fornecedor), o custo do frete (fulfillment, na cotação) e o preço que a loja cobra, com a campanha (catalog). A cotação é pública: a página do produto e o carrinho cotam sem login, então o navegador não pode informar o preço.

## Decisão

Uma opção de frete sai de graça quando a margem dos produtos paga o frete inteiro:

```
preço da loja − custo no fornecedor − taxa de pagamento do pior caso ≥ custo daquele frete
```

- O fulfillment busca o preço da loja no catalog pelo gRPC `BatchGetProducts`, com o `product_id` que vem na cotação. É a mesma chamada que o orders já usa para validar o pedido (ADR 0004), com prazo de 1 s.
- A taxa é a do pior caso, o cartão em 6x (`Shipping:WorstPaymentFeePercent`): o frete é cotado antes de o comprador escolher o pagamento.
- Cada opção é avaliada sozinha: pode sair grátis o Econômico e o Expresso continuar cobrado.
- Sem como garantir a margem, não há frete grátis: item sem `product_id`, produto sem custo conhecido (que não veio do fornecedor) ou catalog fora do ar. A cotação nunca falha por isso.
- O custo do frete para a loja é cotado de novo quando o fornecedor aceita o pedido e fica separado do cobrado, para a margem do pedido (#225, #282) mostrar o frete grátis como custo.

## Alternativas descartadas

- **Limite fixo de valor (frete grátis acima de R$ X):** simples de explicar, mas não garante a margem e pede um número que o dono teria de recalibrar a cada mudança de fornecedor ou de câmbio.
- **O fulfillment espelhar os preços do catalog por eventos:** evitaria a chamada síncrona, mas duplicaria a regra da campanha (preço efetivo) num terceiro serviço, como já existe no orders, e o espelho atrasado daria frete grátis com um preço que já mudou.
- **O preço vir na própria cotação:** o orders poderia informar, mas a página do produto e o carrinho cotam direto pelo BFF, e um preço vindo do cliente seria forjável.

## Consequências

- O fulfillment passa a depender do catalog na cotação (gRPC, `Catalog__GrpcAddress`). A dependência é fraca: com o catalog fora, a cotação sai sem frete grátis.
- O preço que decide o frete grátis é o mesmo que o orders cobra, porque os dois vêm do catalog no momento da chamada. O orders recota o frete ao criar o pedido, então o frete grátis que o comprador viu é conferido no servidor.
- Não há "faltam R$ X para o frete grátis": com a margem, não existe um valor fixo para mostrar.
- Uma campanha agressiva pode tirar o frete grátis de um produto, porque reduz a margem: é o comportamento desejado.
- Com o fornecedor real (#227), a regra continua no fulfillment: o adaptador só precisa cotar o custo do frete e informar o custo dos produtos.
