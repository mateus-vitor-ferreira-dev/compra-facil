# LGPD: o que a loja trata e o que falta antes de vender

Referência para a venda real (#226), a revisar com o contador e com um advogado. **Não é parecer jurídico**: descreve o que o sistema faz hoje, onde cada dado fica e o que precisa de decisão ou de documento antes de a loja vender de verdade.

## Hoje: a demonstração

A loja publicada é uma demonstração (`DEMO_MODE`). O que muda nela (#280):

- O topo da loja e a seção de entrega do checkout pedem para não usar dados reais.
- Os dados pessoais dos pedidos com mais de 7 dias são mascarados (`Demo:AnonymizeAfter`, #367): nome, CPF, telefone e rua no orders e no fulfillment, e o corpo dos e-mails no notifications. O Mailpit da demonstração descarta os e-mails em 7 dias.
- As rotas `/sim` de operação do pagamento simulado ficam fechadas, e a página do PIX só abre pelo código que o comprador recebe (#366).

Na loja de verdade, a retenção da demonstração **fica desligada**: a nota fiscal obriga a guardar os dados (veja "Retenção").

## Os dados, onde ficam e para quê

| Dado | Onde fica | Para quê | Base legal provável (art. 7º) |
|---|---|---|---|
| E-mail e senha (hash) | identity | entrar na conta | execução de contrato |
| E-mail do convidado | identity (`guest_email`), só como contato | o link da compra sem conta (ADR 0008) | execução de contrato |
| Nome, CPF e telefone de quem recebe | orders, fulfillment (pedido ao fornecedor e nota); nome e CPF no payments só até a cobrança | a nota fiscal, a entrega e o pagamento (o PIX do Mercado Pago pede o CPF) | obrigação legal (NF-e) e execução de contrato |
| Endereço de entrega | orders, fulfillment, fornecedor, transportadora | a entrega e a nota | execução de contrato e obrigação legal |
| Cartão | **não fica na loja**: o navegador manda direto ao provedor, que devolve um token (#220) | cobrar | execução de contrato |
| Corpo dos e-mails enviados | notifications | reenvio e suporte | legítimo interesse (a definir o prazo) |
| IP do comprador | gateway (rate limit, em memória no Redis) e logs | segurança e abuso | legítimo interesse |
| Cookies | navegador: sessão, carrinho, CEP | funcionamento | execução de contrato (estritamente necessários) |
| Logs e traces | Grafana Cloud (#162) | operação | legítimo interesse |

Os eventos no Kafka levam o endereço e o CPF (`OrderConfirmed`), e o nome e o CPF de quem paga (`OrderPlaced`), e expiram em 7 dias. O outbox apaga cada evento depois de publicado.

## Operadores e transferência internacional

| Operador | O que recebe | Onde |
|---|---|---|
| Hetzner (VPS, via Coolify) | todos os bancos | Alemanha |
| Cloudflare (Workers, R2) | as páginas, os cookies e as fotos; a loja não guarda dados pessoais no R2 | global |
| Grafana Cloud | logs e traces, que podem ter e-mail e IP | a confirmar na conta |
| Gateway de pagamento (#228) | e-mail, nome, CPF, valor e o cartão tokenizado | Brasil, a confirmar |
| Emissor de NF-e (#229) | nome, CPF e endereço | Brasil, a confirmar |
| Fornecedor e transportadora (#227) | nome, telefone e endereço | Brasil (fornecedor nacional) |
| Provedor de e-mail (Resend) | e-mail e o conteúdo das mensagens | a confirmar |

A transferência para fora do Brasil (Hetzner, Cloudflare e, conforme a região, o Grafana Cloud) precisa de uma base do art. 33. O caminho mais comum são as cláusulas-padrão aprovadas pela ANPD (Resolução CD/ANPD nº 19/2024), no contrato com cada operador. Conferir se os termos de cada provedor já as incluem.

## Retenção

| Dado | Prazo | Motivo |
|---|---|---|
| Nota fiscal e os dados dela (nome, CPF, endereço, valores) | o prazo de guarda dos documentos fiscais, **a confirmar com o contador** (o prazo do CTN costuma ser contado em 5 anos) | obrigação legal |
| Pedido | o mesmo da nota | a nota referencia o pedido |
| Conta | até o titular pedir a exclusão, sem os pedidos (que seguem a nota) | execução de contrato |
| Convidado | o link vale 180 dias (ADR 0008); a conta fica como a do pedido | execução de contrato |
| Corpo dos e-mails | **a definir** (sugestão: 90 dias) | suporte |
| Logs e traces | o padrão do Grafana Cloud free (a confirmar) | operação |

O `Demo:AnonymizeAfter` (#367) é a peça para um prazo de e-mail e de logs. Para a nota e o pedido, o prazo é o fiscal, e o job da demonstração não deve rodar.

## Direitos do titular (art. 18)

A política de privacidade (`/politicas/privacidade`) já lista os direitos, mas falta o canal e o processo:

- [ ] E-mail do encarregado (`LEGAL_EMAIL`) e o nome dele na política (art. 41).
- [ ] Como responder a um pedido de **acesso**: exportar a conta, os pedidos e as notas do titular.
- [ ] Como responder a um pedido de **exclusão**: apagar a conta e mascarar o que não está sob guarda fiscal; explicar o que fica e por quê.
- [ ] Prazo de resposta e registro de cada pedido.

## Antes de vender: checklist

- [ ] Nome empresarial, CNPJ e endereço na política e no rodapé (`LEGAL_*`, #226).
- [ ] Prazos de retenção da tabela acima, com o contador.
- [ ] Política de privacidade revisada por um advogado, sem o aviso de rascunho.
- [ ] Contratos (ou termos aceitos) com cada operador, com as cláusulas-padrão onde houver transferência internacional.
- [ ] Encarregado definido e o canal publicado.
- [ ] Processo de acesso e exclusão (seção anterior).
- [ ] `DEMO_MODE` desligado e `Demo:AnonymizeAfter` fora da configuração de produção.
- [ ] Plano de incidente: quem avisa a ANPD e os titulares, e em quanto tempo (Resolução CD/ANPD nº 15/2024).
- [ ] Revisar o que os logs e os traces levam (e-mail, IP) e mascarar o que não for necessário.
