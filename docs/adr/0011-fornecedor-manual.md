# ADR 0011: Fornecedor manual, operado pela equipe no painel

Status: aceito (16/11/2026)

## Contexto

O ADR 0007 pôs o fornecedor atrás de uma porta (`ISupplierGateway`) e modelou o adaptador no CJ Dropshipping, a única API de fornecedor documentada na pesquisa: catálogo e estoque por API, pedido criado por chamada, rastreio e status por webhook assinado, frete cotado em dólar.

Para a primeira venda (#412), a loja precisa de um fornecedor nacional, e ainda nenhum foi contatado (a prospecção é o agentes-hub#156). A pesquisa no vault não achou API documentada de nenhum fornecedor nacional: o mais provável é uma planilha de produtos e o painel do próprio fornecedor, onde o lojista faz cada pedido à mão. Esperar um fornecedor com API seria adiar a venda por uma integração que talvez nem exista.

## Decisão

Um segundo adaptador, `ManualSupplierGateway`, ligado por `Supplier:Mode=manual`, em que a equipe faz o papel da API:

- **Catálogo e estoque:** a planilha CSV do fornecedor, subida no painel (`/admin/fornecedor`). Ela é conferida linha a linha e, sem nenhum erro, substitui a anterior e passa pelo mesmo `CatalogSync` do feed. O SKU da planilha é o SKU da loja, o custo é em reais, e a categoria é o slug da categoria da loja. A loja vende o estoque da planilha menos uma margem de segurança (`SafetyStock`), porque a planilha envelhece entre uma subida e outra.
- **Frete:** uma tabela da loja em reais, por peso e pela região do CEP, somada ao prazo de manuseio do fornecedor. O `ISupplierGateway` deixa de supor dólar: a moeda vem em cada opção de frete.
- **Pedido:** o pedido pago fica `AwaitingStaff`, na fila "Pedidos a fazer". A equipe faz o pedido no site do fornecedor e registra no painel o número dele e o que pagou. Depois, informa o rastreio e marca a entrega. Cada ação publica o mesmo evento que um fornecedor com API geraria (`SupplierOrderPlaced`, `Shipped`, `Delivered` e `Failed` com `out_of_stock`): o orders, as notificações e o estorno não mudam.
- **Margem:** o que a equipe pagou substitui o custo esperado da planilha e da tabela. O esperado fica guardado, para comparar.
- **Cancelamento:** na fila, estorna como antes. Um pedido já feito no fornecedor só cancela com a confirmação de que a equipe cancelou lá (`cancelled_at_supplier`); sem ela, a loja estornaria o cliente e continuaria pagando o fornecedor.
- **Devolução:** o manual não abre disputa. A devolução cai como recusada na abertura, e a equipe combina com o fornecedor e instrui o cliente pela tela de devoluções (#281).

A fila e os pedidos em andamento também são do papel `auditor`, sem os dados de entrega do cliente, para o Balcão do agentes-hub avisar o pedido parado e a planilha velha (agentes-hub#157).

## Consequências

- A loja vende com qualquer fornecedor, tenha API ou não, a partir da primeira planilha.
- Cada pedido custa trabalho da equipe, e o prazo prometido ao cliente passa a depender dela. A fila mostra a idade de cada pedido e o Balcão avisa depois de 4 h; acima de algumas dezenas de pedidos por dia, vale procurar fornecedor com API.
- O estoque é tão bom quanto a última planilha. A margem de segurança e o aviso de planilha com mais de 3 dias reduzem, mas não eliminam, a venda do que o fornecedor não tem: o "sem estoque" estorna o cliente.
- A tabela de frete é da loja até a primeira cotação real. Errar para menos sai da margem; a #359 e a primeira compra no fornecedor ajustam.
- Trocar para um fornecedor com API é um adaptador novo atrás da mesma porta e uma mudança de `Supplier:Mode`. Os pedidos já feitos no manual seguem pelo painel até a entrega.
