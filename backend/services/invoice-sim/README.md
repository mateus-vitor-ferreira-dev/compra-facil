# invoice-sim

Emissor de NF-e simulado (ADR 0007, #221), no formato da API da **Nuvem Fiscal**: o subconjunto que o
fulfillment usa. É dublê de teste: estado só em memória, comportamento determinístico, sobe no compose e
no CI e não vai para a produção. Trocar pela Nuvem Fiscal de verdade é trocar as URLs e as credenciais (e
conferir os campos na documentação dela: este simulador não foi validado contra a API real).

## Configuração

| Variável | Padrão | Uso |
|---|---|---|
| `PORT` | `8090` | Porta HTTP (no compose, `127.0.0.1:8094`). |
| `SIM_CLIENT_ID`, `SIM_CLIENT_SECRET` | `sim-client`, `sim-secret` | As credenciais do `POST /oauth/token`. |
| `SIM_TOKEN_TTL` | `1h` | Validade do token. |
| `SIM_ALLOWED_CFOP` | `5120,6120,1202,2202` | CFOPs aceitos (venda à ordem e devolução, dentro e fora do estado). |
| `SIM_CANCEL_WINDOW` | `24h` | Até quando a nota autorizada pode ser cancelada. |

## API

| Método e rota | Observação |
|---|---|
| `POST /oauth/token` | `grant_type=client_credentials`, `client_id`, `client_secret` (form). Na Nuvem Fiscal, em `auth.nuvemfiscal.com.br`. |
| `POST /nfe` | `{ambiente, referencia, infNFe}` com as tags da NF-e (`ide`, `emit`, `dest`, `det[].prod`, `total.ICMSTot`, `pag.detPag`). Sempre 200: o `status` diz se a SEFAZ autorizou (`autorizado`, com `chave` de 44 dígitos e protocolo) ou rejeitou (`rejeitado`, com `autorizacao.codigo_status` e `motivo_status`). A mesma `referencia` no mesmo ambiente devolve a mesma nota. |
| `GET /nfe?ambiente=&referencia=` | `{data: [nota]}`: para a loja achar a nota de um pedido depois de um timeout. |
| `GET /nfe/{id}` | |
| `POST /nfe/{id}/cancelamento` | `{justificativa}` de 15 a 255 caracteres. Só a autorizada, dentro do prazo; a nota fica `cancelado`. Repetir devolve o mesmo evento. |
| `GET /nfe/{id}/cancelamento` | |
| `GET /nfe/{id}/xml`, `GET /nfe/{id}/pdf` | O `nfeProc` (sem assinatura) e um DANFE simplificado. A nota rejeitada não tem nenhum dos dois (404). |

Erros no formato da Nuvem Fiscal: `{"error": {"code", "message"}}`.

### O que a SEFAZ simulada confere

| cStat | Rejeição |
|---|---|
| 207 | CNPJ do emitente inválido |
| 237 | CPF do destinatário inválido (dígitos verificadores) |
| 225 | Falha no schema: sem nome, CEP sem 8 dígitos, UF inválida, sem itens |
| 778 | NCM sem 8 dígitos |
| 543 | CFOP fora de `SIM_ALLOWED_CFOP` |
| 610 | Total da nota diferente da soma dos itens |
| 866 | Pagamentos menores que o total (não vale para a devolução, que não recebe nada) |
| 267 | Devolução (`finNFe` 4) sem `NFref.refNFe` de uma nota autorizada (código próprio do simulador) |
| 999 | E-mail do destinatário com `+recusar-nota` (para testar a compensação) |

## Rotas de demonstração (sem token)

| Rota | |
|---|---|
| `GET /sim/notas` | Todas as notas, da mais nova para a mais velha. |
| `POST /sim/reset` | Apaga as notas. A numeração parte do horário: reiniciado, o simulador não repete número nem id. |
