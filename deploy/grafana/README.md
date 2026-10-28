# Grafana Cloud: dashboards e alertas

A telemetria da produção vai para o Grafana Cloud (plano gratuito) pelo Collector do compose
([`../coolify/otel-collector.yaml`](../coolify/otel-collector.yaml)). Esta pasta põe lá o que se olha e o
que avisa (#162):

- os dashboards de [`infra/grafana/dashboards`](../../infra/grafana/dashboards), os mesmos do local, com os
  datasources trocados pelos do Grafana Cloud;
- os alertas de [`alertas.yaml`](alertas.yaml), por e-mail, numa pasta **CompraFácil**.

Quem publica é o [`publicar.py`](publicar.py), pela API do Grafana. O workflow
[`grafana.yml`](../../.github/workflows/grafana.yml) roda o script quando um dos dois muda na `main`.
No painel, os alertas aparecem travados: a mudança é aqui, por PR.

## Os alertas

| Alerta | Quando | Severidade |
|---|---|---|
| Gateway respondendo 5xx | uma rota com 5xx em mais de 5% das respostas (e pelo menos 5) em 5 min | crítica |
| Catálogo fora | 502/503/504 do gateway na rota do catalog, ou o catalog sem telemetria | crítica |
| Serviço sem telemetria | um dos outros serviços (ou o gateway) parou de mandar métricas | crítica |
| Consumer parado | um consumer group não zera o lag há 15 min | alta |
| Mensagem na DLT | uma mensagem foi para a DLT nos últimos 10 min | alta |
| Backup atrasado | o último backup inteiro tem mais de 26 h, ou não há backup em 3 dias | alta |
| Backup falhou | a última rodada do `pg-backup` terminou com erro | alta |

Cada alerta vai direto para o contato `compra-facil-email` (sem mexer na árvore de notificação do
stack), agrupado por alerta, e repete a cada 4 h enquanto durar. Quando resolve, chega um e-mail de
resolvido.

## Uma vez, no Grafana Cloud

1. **Administration → Users and access → Service accounts**: uma service account `compra-facil-ci` com o
   papel **Editor** e um token. É ele que publica.
2. No GitHub (**Settings → Secrets and variables → Actions**):

   | Tipo | Nome | Valor |
   |---|---|---|
   | Secret | `GRAFANA_CLOUD_TOKEN` | o token da service account |
   | Variable | `GRAFANA_CLOUD_URL` | `https://<stack>.grafana.net`, sem barra no fim |
   | Variable | `ALERT_EMAIL` | quem recebe os alertas (vírgula para mais de um) |

3. **Actions → grafana → Run workflow**, para a primeira publicação. Enquanto `GRAFANA_CLOUD_URL` não
   existir, o workflow não roda.

O e-mail sai pelo próprio Grafana Cloud, sem SMTP nosso.

Da máquina, o mesmo:

```bash
GRAFANA_URL=https://<stack>.grafana.net GRAFANA_TOKEN=glsa_... ALERT_EMAIL=voce@exemplo.com \
  python3 deploy/grafana/publicar.py
```

Os datasources do Grafana Cloud têm os uids `grafanacloud-prom`, `grafanacloud-traces` e
`grafanacloud-logs`. Se o stack tiver outros, `GRAFANA_DS_PROMETHEUS`, `GRAFANA_DS_TEMPO` e
`GRAFANA_DS_LOKI` mudam a escolha (o uid aparece na URL do datasource, em **Connections → Data sources**).

## Ensaio local

O script fala com qualquer Grafana, então dá para ensaiar com a stack local e o Mailpit no lugar do
e-mail. Um Grafana à parte, para não misturar com o do compose:

```bash
REDIS_PORT=56379 docker compose up -d --wait
docker run -d --rm --name grafana-ensaio --network comprafacil_default -p 127.0.0.1:3011:3000 \
  -e GF_AUTH_ANONYMOUS_ENABLED=true -e GF_AUTH_ANONYMOUS_ORG_ROLE=Admin \
  -e GF_SMTP_ENABLED=true -e GF_SMTP_HOST=mailpit:1025 -e GF_SMTP_FROM_ADDRESS=grafana@comprafacil.local \
  -v "$PWD/infra/grafana/provisioning/datasources:/etc/grafana/provisioning/datasources:ro" \
  grafana/grafana:13.2.4
GRAFANA_URL=http://127.0.0.1:3011 ALERT_EMAIL=alertas@comprafacil.local python3 deploy/grafana/publicar.py

docker compose stop payments   # em uns 7 minutos, "Serviço sem telemetria" no Mailpit (localhost:8025)
docker compose start payments  # e depois o e-mail de resolvido
docker stop grafana-ensaio
```

Os serviços mandam métricas a cada exportação, com ou sem tráfego, e a série some da consulta 5 minutos
depois da última: é esse o atraso do aviso de serviço fora.

## Em produção, o ensaio de verdade

Com tudo publicado, pare um serviço pelo painel do Coolify (o `payments`, que não derruba a vitrine) e
espere o e-mail. Suba de novo e espere o de resolvido.

## A loja (Cloudflare Workers)

O Worker da loja não passa pelo Collector: os traces e os logs dele saem pela observabilidade da
Cloudflare. Para chegarem ao Grafana Cloud, em **Workers & Pages → Observability → Destinations**, dois
destinos OTLP com o mesmo cabeçalho do Collector (`Authorization: Basic <GRAFANA_CLOUD_OTLP_AUTH>`):

| Destino | Endpoint |
|---|---|
| `grafana-traces` | `<GRAFANA_CLOUD_OTLP_ENDPOINT>/v1/traces` |
| `grafana-logs` | `<GRAFANA_CLOUD_OTLP_ENDPOINT>/v1/logs` |

Depois de criados, os nomes deles no `frontend/web/wrangler.jsonc`, em `observability`, por PR:

```jsonc
"observability": {
  "enabled": true,
  "traces": { "enabled": true, "destinations": ["grafana-traces"] },
  "logs": { "enabled": true, "destinations": ["grafana-logs"] },
},
```
