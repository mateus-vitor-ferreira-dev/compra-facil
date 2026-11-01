# ADR 0008: Compra sem conta com conta de convidado e link por e-mail

Status: aceito (31/10/2026)

## Contexto

O checkout exigia login. Baymard e NN/g recomendam comprar como visitante, e uma pesquisa da Opinion Box de 2026 mostra que parte de quem costuma desistir cita a conta obrigatória (9%). O pedido, a desistência, a nota e o acompanhamento ao vivo dependem de um token com `sub`, `email` e papel `customer`.

## Decisão

Cada compra sem conta cria uma conta própria no identity, sem senha, com o papel `customer`. O e-mail digitado fica em `guest_email`, fora do login; `email` e `user_name` guardam marcadores que ninguém digita. Os outros serviços não mudam.

O comprador volta à compra por um link enviado por e-mail, guardado só como hash, válido por 180 dias e reutilizável. A sessão só abre no clique, numa página sem envio de referrer. O reenvio responde sempre 202, com um link por compra a cada 5 minutos, no máximo.

A chave do formulário do checkout identifica o convidado, e o mesmo formulário reenviado não duplica o pedido.

## Consequências

- Digitar um e-mail nunca dá acesso a compras ou contas existentes com ele.
- O e-mail do convidado não é verificado: alguém pode digitar o de outra pessoa, que recebe o link e os avisos. O limite é o rate limit por IP do gateway.
- Quem tem o link vê os dados da compra enquanto ele valer.
- Os pedidos de convidado não passam para uma conta criada depois com o mesmo e-mail.
- O link sai antes de o pedido ser aceito: se o pedido falhar depois disso, o e-mail chega mesmo assim.
