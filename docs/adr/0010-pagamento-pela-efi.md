# ADR 0010: Pagamento pela Efí, com o cartão cobrado em dois passos

Status: aceito (05/11/2026)

## Contexto

O ADR 0007 pôs o pagamento atrás de um adaptador (`IPaymentProvider`) e escolheu o Mercado Pago, com o cartão autorizado no checkout e capturado quando o fornecedor aceitasse o pedido. A conta do Mercado Pago do dono foi bloqueada junto com a do Mercado Livre, e o gateway da venda de verdade passou a ser a Efí (#379), que cobra 1,19% no PIX e 3,49% no crédito à vista e tem conta PJ dentro do login da PF.

A Efí muda quatro coisas que o desenho supunha:

1. **Não separa autorizar de capturar.** O cartão é cobrado no checkout. Aprovado pela operadora, fica `approved`, e vira `paid` quando é liquidado.
2. **Não tem chave de idempotência no cartão.** Repetir o one-step depois de uma queda cobraria duas vezes, ou seria recusado por token já usado, e o pedido seria cancelado com o cliente cobrado.
3. **Não estorna nem cancela o cartão `approved`.** O estorno só vale em `paid`, e o cancelamento em `new`, `waiting` ou `unpaid`.
4. **Não avisa a validade do PIX que passa.** A cobrança continua `ATIVA`; o banco só não aceita mais o pagamento.

As notificações também são diferentes: o PIX vem num webhook por chave, que exige mTLS ou o segredo na URL. O cartão vem só com um token, sem assinatura, e o integrador consulta o que mudou.

## Decisão

O adaptador da Efí (`EfiProvider`) substitui o do Mercado Pago, e o payments se ajusta ao que ela permite:

- **Cartão em dois passos.** O payments abre a cobrança (`POST /v1/charge`), grava a referência e só então paga (`/pay`). Pagar de novo uma cobrança que já tem forma de pagamento é recusado pela Efí (`3500030`): a nova tentativa só lê o resultado. O token do cartão fica guardado entre os dois passos e é apagado depois.
- **`approved` é `Authorized`, `paid` é `Approved`.** O pedido segue na aprovação, como seguia na autorização do Mercado Pago (`PaymentApproved` com `captured: false`). A liquidação chega pela notificação, e a captura no `SupplierOrderPlaced` deixa de existir.
- **A devolução espera a liquidação.** Pedida com o cartão `approved`, ela fica em `Payment.PendingReversal` e sai quando a notificação de `paid` chega. O cartão em análise e o PIX não pago são cancelados no provedor.
- **O payments confere o PIX vencido.** Um job olha o PIX pendente que passou da validade: se foi pago no último instante, aprova; senão, remove a cobrança na Efí e publica `PaymentFailed` (`pix_expired`).
- **Webhooks sem confiar no corpo.** O do PIX é cadastrado pelo payments ao subir, sem a validação de mTLS (`x-skip-mtls-checking`), com um segredo na URL. O do cartão consulta `GET /v1/notification/:token`. Nos dois, a cobrança é consultada na Efí.
- **O navegador tokeniza o cartão** com a biblioteca `payment-token-efi`. Na demonstração, sem a Efí, o payment-sim faz o papel dela no mesmo formato (`/sim/card-token`).
- **O payment-sim fala o contrato da Efí**, com as regras da homologação dela: o último dígito do cartão decide, e a devolução de R$ 0,01 é recusada.

## Alternativas descartadas

- **One-step com a mesma chamada repetida:** é o exemplo da documentação, mas sem idempotência uma queda depois de cobrar vira cobrança dupla, ou pedido cancelado com o cliente cobrado.
- **Procurar a cobrança pelo `custom_id` antes de cobrar de novo:** a listagem da Efí que filtra por ele ainda é beta, e não há garantia de que a cobrança recém-criada já apareça nela.
- **Deixar a mensagem falhar e repetir até a liquidação:** os retries da mensageria (ADR 0003) acabam na dead letter, e a janela entre `approved` e `paid` não é documentada. Guardar a devolução pendente no pagamento não depende de quanto ela dura.
- **mTLS no gateway para o webhook do PIX:** exigiria certificado de cliente no Traefik só para uma rota. O segredo na URL, com a consulta à Efí como verdade, protege o mesmo.
- **Manter os dois adaptadores:** o do Mercado Pago não tem conta para usar. Ele fica no histórico (#220) e sai do código.

## Consequências

- A tarifa sai do valor recebido e não volta no estorno. No PIX, o Vintém mediu isso com dinheiro real numa conta Efí (vintemhq/vintem, `docs/EFI.md`, 2026-10): 1,19% no recebimento (R$ 0,12 num PIX de R$ 10,00) e nenhuma tarifa na devolução. A margem já trata assim: guarda a taxa do pagamento aprovado e não a desfaz no estorno. A tarifa do estorno do cartão continua sem fonte; confirmar no contrato da conta.
- Como a tarifa já saiu, a devolução integral do PIX precisa de saldo na conta. Sem saldo, a Efí aceita o pedido e depois termina `NAO_REALIZADO`, sem motivo (também medido pelo Vintém).
- O estorno do cartão também não tem id nem idempotência na Efí. Se o payments cair entre pedir o estorno e gravá-lo, a nova tentativa pode ser recusada ("já existe um estorno em processamento"), e a equipe confere no painel da Efí. A janela é de milissegundos, e o erro aparece no log.
- A devolução do PIX recusada depois de aceita (`NAO_REALIZADO`) já foi contada como estornada: hoje o webhook só registra o erro para a equipe devolver pelo painel. Precisa virar um estado visível no pedido e no painel antes da primeira venda (#396).
- O estorno parcial do cartão vale um por dia por cobrança, e só depois da liquidação; o painel mostra o motivo quando a Efí recusa.
- Produção depende de três coisas do dono: as credenciais da aplicação, o certificado `.p12` da API Pix e o identificador de conta no Worker (`deploy/README.md`).
