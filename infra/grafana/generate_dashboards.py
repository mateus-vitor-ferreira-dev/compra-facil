"""Gera os dashboards do Grafana (infra/grafana/dashboards/*.json).

Os JSON são versionados e provisionados na subida; este script é a fonte para mantê-los no mesmo
padrão. Rodar: python3 infra/grafana/generate_dashboards.py
"""
import json
import pathlib

OUT = pathlib.Path(__file__).parent / "dashboards"
DS = {"type": "prometheus", "uid": "prometheus"}


def target(expr, legend="", ref="A", instant=False):
    # histogramas de duração mostram exemplars: o ponto no gráfico abre o trace que o gerou
    return {"datasource": DS, "expr": expr, "legendFormat": legend, "refId": ref, "instant": instant, "range": not instant,
            "exemplar": "_duration_seconds_bucket" in expr and not instant}


def panel(kind, title, targets, x, y, w, h, unit=None, description="", thresholds=None, stack=False, decimals=None):
    p = {
        "type": kind,
        "title": title,
        "description": description,
        "datasource": DS,
        "gridPos": {"x": x, "y": y, "w": w, "h": h},
        "targets": [t if isinstance(t, dict) else target(*t) for t in targets],
        "fieldConfig": {"defaults": {}, "overrides": []},
        "options": {},
    }
    d = p["fieldConfig"]["defaults"]
    if unit:
        d["unit"] = unit
    if decimals is not None:
        d["decimals"] = decimals
    if thresholds:
        d["thresholds"] = {"mode": "absolute", "steps": thresholds}
        d["color"] = {"mode": "thresholds"}
    if kind == "timeseries":
        d["custom"] = {"lineWidth": 2, "fillOpacity": 15 if stack else 5, "showPoints": "never",
                       "stacking": {"mode": "normal" if stack else "none"}}
        p["options"] = {"legend": {"displayMode": "list", "placement": "bottom"}, "tooltip": {"mode": "multi", "sort": "desc"}}
    if kind == "stat":
        p["options"] = {"reduceOptions": {"calcs": ["lastNotNull"], "fields": "", "values": False},
                        "colorMode": "value", "graphMode": "area", "textMode": "auto"}
    return p


def row(title, y):
    return {"type": "row", "title": title, "collapsed": False, "gridPos": {"x": 0, "y": y, "w": 24, "h": 1}, "panels": []}


def dashboard(uid, title, description, panels, variables=(), refresh="10s", time_from="now-30m"):
    for i, p in enumerate(panels, start=1):
        p["id"] = i
    return {
        "uid": uid,
        "title": title,
        "description": description,
        "tags": ["compra-facil"],
        "timezone": "browser",
        "schemaVersion": 39,
        "version": 1,
        "editable": False,
        "refresh": refresh,
        "time": {"from": time_from, "to": "now"},
        "templating": {"list": list(variables)},
        "annotations": {"list": []},
        "panels": panels,
    }


GOOD, WARN, BAD = "green", "orange", "red"

# ---------------------------------------------------------------- 1. RED por serviço
service_var = {
    "name": "service", "label": "Serviço", "type": "query", "datasource": DS,
    "query": {"query": "label_values(http_server_request_duration_seconds_count, service_name)", "refId": "service"},
    "includeAll": True, "multi": True, "current": {"text": "All", "value": "$__all"}, "refresh": 2,
}
SEL = 'service_name=~"$service"'
red = dashboard(
    "compra-facil-red", "CompraFácil — Serviços (RED)",
    "Taxa, erros e duração (RED) de cada serviço, pela instrumentação OpenTelemetry.",
    [
        row("HTTP (entrada de cada serviço)", 0),
        panel("timeseries", "Requisições por segundo", [(
            f"sum by (service_name) (rate(http_server_request_duration_seconds_count{{{SEL}}}[$__rate_interval]))", "{{service_name}}")],
            0, 1, 8, 8, unit="reqps"),
        panel("timeseries", "Erros (5xx) %", [(
            f'100 * sum by (service_name) (rate(http_server_request_duration_seconds_count{{{SEL}, http_response_status_code=~"5.."}}[$__rate_interval]))'
            f" / sum by (service_name) (rate(http_server_request_duration_seconds_count{{{SEL}}}[$__rate_interval]))", "{{service_name}}")],
            8, 1, 8, 8, unit="percent", description="Fração das respostas com status 5xx."),
        panel("timeseries", "Duração p95", [(
            f"histogram_quantile(0.95, sum by (le, service_name) (rate(http_server_request_duration_seconds_bucket{{{SEL}}}[$__rate_interval])))", "{{service_name}}")],
            16, 1, 8, 8, unit="s"),
        panel("timeseries", "Duração p95 por rota", [(
            f'histogram_quantile(0.95, sum by (le, service_name, http_route) (rate(http_server_request_duration_seconds_bucket{{{SEL}, http_route!=""}}[$__rate_interval])))',
            "{{service_name}} {{http_route}}")], 0, 9, 24, 8, unit="s"),
        row("Spans (todos os tipos, gerado pelo Tempo a partir dos traces)", 17),
        panel("timeseries", "Consumers Kafka: duração p95 por evento", [(
            'histogram_quantile(0.95, sum by (le, service, span_name) (rate(traces_spanmetrics_latency_bucket{span_kind="SPAN_KIND_CONSUMER"}[$__rate_interval])))',
            "{{service}} {{span_name}}")], 0, 18, 12, 8, unit="s"),
        panel("timeseries", "Spans com erro por serviço", [(
            'sum by (service) (rate(traces_spanmetrics_calls_total{status_code="STATUS_CODE_ERROR"}[$__rate_interval]))', "{{service}}")],
            12, 18, 12, 8, unit="ops"),
    ],
    variables=[service_var],
)

# ---------------------------------------------------------------- 2. SAGA de compra
FIN = "compra_facil_orders_finished_total"
saga = dashboard(
    "compra-facil-saga", "CompraFácil — SAGA de compra",
    "Pedidos do início ao fim: confirmados (SAGA concluída) × cancelados (compensada), por motivo, e a saúde dos consumers Kafka.",
    [
        row("Resultado dos pedidos (no período)", 0),
        panel("stat", "Pedidos criados", [("sum(increase(compra_facil_orders_placed_total[$__range])) or vector(0)", "", "A", True)], 0, 1, 5, 5, decimals=0),
        panel("stat", "Confirmados", [(f'sum(increase({FIN}{{status="Confirmed"}}[$__range])) or vector(0)', "", "A", True)], 5, 1, 5, 5,
              decimals=0, thresholds=[{"color": GOOD, "value": None}]),
        panel("stat", "Cancelados (compensados)", [(f'sum(increase({FIN}{{status="Cancelled"}}[$__range])) or vector(0)', "", "A", True)], 10, 1, 5, 5,
              decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 1}]),
        panel("stat", "% compensadas", [(
            f'100 * sum(increase({FIN}{{status="Cancelled"}}[$__range])) / sum(increase({FIN}[$__range]))', "", "A", True)], 15, 1, 4, 5,
              unit="percent", decimals=1, thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 10}, {"color": BAD, "value": 30}]),
        panel("stat", "Receita confirmada", [("(sum(increase(compra_facil_orders_confirmed_amount_total[$__range])) / 100) or vector(0)", "", "A", True)],
              19, 1, 5, 5, unit="currencyBRL", decimals=2),
        panel("timeseries", "Pedidos finalizados por status e motivo", [(
            f"sum by (status, reason) (rate({FIN}[$__rate_interval]))", "{{status}} {{reason}}")], 0, 6, 12, 8, unit="ops", stack=True),
        panel("timeseries", "Cobranças por resultado", [(
            "sum by (result, reason) (rate(compra_facil_payments_charges_total[$__rate_interval]))", "{{result}} {{reason}}")],
              12, 6, 6, 8, unit="ops", stack=True),
        panel("timeseries", "Reservas de estoque", [(
            "sum by (result) (rate(compra_facil_inventory_reservations_total[$__rate_interval]))", "{{result}}")], 18, 6, 6, 8, unit="ops", stack=True),
        row("Kafka e consumers", 14),
        panel("timeseries", "Lag dos consumers (mensagens atrás)", [(
            "sum by (group) (kafka_consumer_group_lag_sum_ratio)", "{{group}}")], 0, 15, 8, 8, decimals=0,
              description="Quantas mensagens cada consumer group ainda não processou (lido do Redpanda pelo Collector)."),
        panel("timeseries", "Eventos consumidos por resultado", [(
            "sum by (result) (rate(compra_facil_messaging_consumed_total[$__rate_interval]))", "{{result}}")], 8, 15, 8, 8, unit="ops", stack=True,
              description="processed, duplicate (inbox), skipped (sem handler), error (cada tentativa que falhou), "
                          "retried (foi para um tópico de retry) e dead_lettered (foi para a DLT; ADR 0003)."),
        panel("stat", "Para a DLT", [(
            'sum(increase(compra_facil_messaging_consumed_total{result="dead_lettered"}[$__range])) or vector(0)', "", "A", True)], 16, 15, 4, 4,
              decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": BAD, "value": 1}],
              description="Esgotaram as tentativas ou estão fora do contrato. Ver e reprocessar: task dlt -- <serviço>."),
        panel("stat", "Para retry", [(
            'sum(increase(compra_facil_messaging_consumed_total{result="retried"}[$__range])) or vector(0)', "", "A", True)], 20, 15, 4, 4,
              decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 1}],
              description="Falharam nas tentativas no lugar e foram para <serviço>.retry-N."),
        panel("stat", "E-mails enviados", [(
            'sum(increase(compra_facil_notifications_emails_total{result="Sent"}[$__range])) or vector(0)', "", "A", True)], 16, 19, 8, 4, decimals=0),
        panel("timeseries", "Tratamento de evento p95", [(
            "histogram_quantile(0.95, sum by (le, service_name, event_type) (rate(compra_facil_messaging_process_duration_seconds_bucket[$__rate_interval])))",
            "{{service_name}} {{event_type}}")], 0, 23, 24, 8, unit="s"),
        row("Catálogo fora (fallback do orders, #81)", 31),
        panel("timeseries", "Pedidos precificados pela cópia local", [(
            "sum by (reason) (rate(compra_facil_orders_catalog_fallback_total[$__rate_interval]))", "{{reason}}")],
              0, 32, 12, 8, unit="ops", stack=True,
              description="O catalog não respondeu (unavailable, timeout) ou o circuito estava aberto (circuit_open): "
                          "o preço saiu da cópia local que o orders mantém pelos eventos."),
        panel("timeseries", "Retries e circuit breaker (Polly)", [(
            'sum by (event_name) (increase(resilience_polly_strategy_events_total{pipeline_name="catalog-grpc", event_name!="PipelineExecuting"}[$__rate_interval]))',
            "{{event_name}}")], 12, 32, 12, 8, decimals=0,
              description="OnRetry: nova tentativa no catalog; OnCircuitOpened/HalfOpened/Closed: estado do circuito."),
    ],
)

# ---------------------------------------------------------------- 3. Campanha (catálogo)
HIT = 'sum(rate(catalog_cache_lookups_total{result="hit"}[$__rate_interval]))'
LOOK = 'sum(rate(catalog_cache_lookups_total{result=~"hit|miss"}[$__rate_interval]))'
campaign = dashboard(
    "compra-facil-campanha", "CompraFácil — Campanha (catálogo)",
    "O pico de leitura das campanhas: RPS e p95 do catálogo, rate limit do gateway, cache e réplicas (critérios do parecer).",
    [
        panel("stat", "RPS no catálogo", [(
            'sum(rate(http_server_request_duration_seconds_count{service_name="catalog"}[1m]))', "", "A", True)], 0, 0, 5, 5, unit="reqps", decimals=1,
              thresholds=[{"color": "blue", "value": None}],
              description="Medido no próprio catalog: conta o tráfego pelo gateway e o direto (k6, gRPC não entra)."),
        panel("stat", "p95 do catálogo", [(
            'histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="catalog"}[5m])))', "", "A", True)],
              5, 0, 5, 5, unit="s", thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 0.2}, {"color": BAD, "value": 0.5}]),
        panel("stat", "Hit ratio do cache", [(f"{HIT} / {LOOK}", "", "A", True)], 10, 0, 5, 5, unit="percentunit", decimals=1,
              thresholds=[{"color": BAD, "value": None}, {"color": WARN, "value": 0.7}, {"color": GOOD, "value": 0.9}],
              description="Meta do #67: acima de 90% no cenário do k6."),
        panel("stat", "Réplicas do catálogo", [('count(target_info{job="compra-facil/catalog"})', "", "A", True)], 15, 0, 4, 5, decimals=0),
        panel("stat", "Recusados pelo rate limit (429)", [(
            'sum(increase(traefik_service_requests_total{service="catalog@file", code="429"}[$__range])) or vector(0)', "", "A", True)], 19, 0, 5, 5,
              decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 1}]),
        panel("timeseries", "RPS no catálogo, por status", [
            ('sum by (http_response_status_code) (rate(http_server_request_duration_seconds_count{service_name="catalog"}[$__rate_interval]))',
             "{{http_response_status_code}}", "A"),
            ('sum(rate(traefik_service_requests_total{service="catalog@file", code="429"}[$__rate_interval]))', "429 (gateway)", "B"),
        ], 0, 5, 12, 8, unit="reqps",
              description="Respostas do catalog por status, mais os 429 que o rate limit do gateway devolve sem chegar ao serviço."),
        panel("timeseries", "Duração do catálogo (p50, p95, p99)", [
            ('histogram_quantile(0.50, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="catalog"}[$__rate_interval])))', "p50", "A"),
            ('histogram_quantile(0.95, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="catalog"}[$__rate_interval])))', "p95", "B"),
            ('histogram_quantile(0.99, sum by (le) (rate(http_server_request_duration_seconds_bucket{service_name="catalog"}[$__rate_interval])))', "p99", "C"),
        ], 12, 5, 12, 8, unit="s"),
        panel("timeseries", "Cache: hit ratio por tipo de leitura", [(
            'sum by (kind) (rate(catalog_cache_lookups_total{result="hit"}[$__rate_interval])) / sum by (kind) (rate(catalog_cache_lookups_total{result=~"hit|miss"}[$__rate_interval]))',
            "{{kind}}")], 0, 13, 12, 8, unit="percentunit"),
        panel("timeseries", "Cache: consultas por resultado", [(
            'sum by (result) (rate(catalog_cache_lookups_total[$__rate_interval]))', "{{result}}")], 12, 13, 12, 8, unit="ops", stack=True,
              description="error = Redis fora (a leitura cai no banco)."),
        row("Teste de carga (k6: task load / task experiment)", 21),
        panel("timeseries", "k6: requisições por cenário", [(
            "sum by (scenario) (rate(k6_http_reqs_total[$__rate_interval]))", "{{scenario}}")], 0, 22, 8, 8, unit="reqps", stack=True,
              description="browse = leitura do catálogo (90%); buy = compras (10%). A rodada do experimento vem no label round."),
        panel("timeseries", "k6: p95 por cenário (visto pelo cliente)", [(
            "max by (scenario) (k6_http_req_duration_p95{name!=\"setup\"})", "{{scenario}}")], 8, 22, 8, 8, unit="s",
              description="p95 de cada intervalo de envio do k6 (maior entre as rotas do cenário). Meta da #89: < 300 ms."),
        panel("timeseries", "k6: usuários virtuais e requisições não enviadas", [
            ("sum(k6_vus)", "VUs", "A"),
            ("sum(rate(k6_dropped_iterations_total[$__rate_interval]))", "não enviadas/s (saturação)", "B"),
        ], 16, 22, 8, 8),
    ],
    refresh="5s", time_from="now-15m",
)

# ---------------------------------------------------------------- 0. Início (home do Grafana)
TEMPO = {"type": "tempo", "uid": "tempo"}
LOKI = {"type": "loki", "uid": "loki"}
START_TEXT = """\
### Por onde começar

| | |
|---|---|
| **Saúde dos serviços** | [Serviços (RED)](/d/compra-facil-red): taxa, erros e latência por serviço e rota |
| **Pedidos e mensageria** | [SAGA de compra](/d/compra-facil-saga): confirmados × compensados, lag, retry/DLT, catálogo fora |
| **Pico de campanha** | [Campanha](/d/compra-facil-campanha): RPS, p95, cache, réplicas e o k6 |

### Investigar uma requisição

Toda resposta da API traz o header **`X-Trace-Id`**, e todo erro traz o campo `trace_id`:

- **no terminal:** `task trace -- <trace_id>` abre o trace aqui;
- **na mão:** *Explore → Tempo* e cole o id no campo TraceQL.

Do trace se vai para os logs do mesmo trace (botão *Logs for this span*), e de cada log de volta para o trace.
Logs de um serviço: `task grafana:logs -- orders`.
"""
home = dashboard(
    "compra-facil-inicio", "CompraFácil — Início",
    "Ponto de partida: saúde dos serviços, erros recentes (traces e logs) e atalhos para os outros dashboards.",
    [
        {"type": "text", "title": "", "gridPos": {"x": 0, "y": 0, "w": 12, "h": 11},
         "options": {"mode": "markdown", "content": START_TEXT}},
        panel("stat", "Requisições/s por serviço (5 min)", [(
            "sum by (service_name) (rate(http_server_request_duration_seconds_count[5m]))", "{{service_name}}", "A", True)],
              12, 0, 12, 5, unit="reqps", decimals=1),
        panel("stat", "Erros 5xx por serviço (5 min)", [(
            'sum by (service_name) (increase(http_server_request_duration_seconds_count{http_response_status_code=~"5.."}[5m])) '
            "or sum by (service_name) (0 * rate(http_server_request_duration_seconds_count[5m]))", "{{service_name}}", "A", True)],
              12, 5, 12, 6, decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": BAD, "value": 1}]),
        {"type": "table", "title": "Traces recentes com erro", "datasource": TEMPO,
         "description": "Clique no trace para abrir a cascata de spans.",
         "gridPos": {"x": 0, "y": 11, "w": 12, "h": 10},
         "targets": [{"datasource": TEMPO, "refId": "A", "queryType": "traceql", "query": "{ status = error }", "limit": 20}],
         "fieldConfig": {"defaults": {}, "overrides": []}, "options": {}},
        {"type": "logs", "title": "Erros e avisos recentes (logs)", "datasource": LOKI,
         "gridPos": {"x": 12, "y": 11, "w": 12, "h": 10},
         "targets": [{"datasource": LOKI, "refId": "A",
                      "expr": '{service_namespace="compra-facil"} | detected_level=~"error|warn"'}],
         "fieldConfig": {"defaults": {}, "overrides": []},
         "options": {"showTime": True, "wrapLogMessage": True, "sortOrder": "Descending", "enableLogDetails": True}},
        row("Compras na última hora", 21),
        panel("stat", "Pedidos criados", [("sum(increase(compra_facil_orders_placed_total[1h])) or vector(0)", "", "A", True)],
              0, 22, 6, 4, decimals=0),
        panel("stat", "Confirmados", [(f'sum(increase({FIN}{{status="Confirmed"}}[1h])) or vector(0)', "", "A", True)],
              6, 22, 6, 4, decimals=0, thresholds=[{"color": GOOD, "value": None}]),
        panel("stat", "Cancelados", [(f'sum(increase({FIN}{{status="Cancelled"}}[1h])) or vector(0)', "", "A", True)],
              12, 22, 6, 4, decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": WARN, "value": 1}]),
        panel("stat", "Mensagens na DLT", [(
            'sum(increase(compra_facil_messaging_consumed_total{result="dead_lettered"}[1h])) or vector(0)', "", "A", True)],
              18, 22, 6, 4, decimals=0, thresholds=[{"color": GOOD, "value": None}, {"color": BAD, "value": 1}],
              description="Ver e reprocessar: task dlt -- <serviço>."),
    ],
    refresh="10s", time_from="now-1h",
)

OUT.mkdir(exist_ok=True)
for name, d in [("inicio", home), ("servicos-red", red), ("saga", saga), ("campanha", campaign)]:
    (OUT / f"{name}.json").write_text(json.dumps(d, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")
    print(f"{name}.json: {len([p for p in d['panels'] if p['type'] != 'row'])} painéis")
