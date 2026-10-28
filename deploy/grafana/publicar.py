#!/usr/bin/env python3
# /// script
# requires-python = ">=3.10"
# dependencies = ["pyyaml==6.0.3"]
# ///
"""Publica no Grafana Cloud os dashboards de infra/grafana/dashboards e os alertas de alertas.yaml (#162).

Usa a API de provisionamento do Grafana, então serve também para o Grafana local (o ensaio do README).
Pode rodar quantas vezes quiser: os dashboards são sobrescritos, e o grupo de regras é trocado inteiro
(uma regra que saiu do arquivo sai do Grafana).

  GRAFANA_URL=https://<stack>.grafana.net GRAFANA_TOKEN=glsa_... ALERT_EMAIL=voce@exemplo.com \\
    python3 deploy/grafana/publicar.py

Precisa do PyYAML; sem ele, `uv run` ou `pipx run` no lugar de `python3` instalam (o cabeçalho acima).

Variáveis:
  GRAFANA_URL    o Grafana (sem barra no fim)
  GRAFANA_TOKEN  token de uma service account com o papel Editor (vazio no Grafana local, que é anônimo)
  ALERT_EMAIL    quem recebe os alertas (vírgula para mais de um)
  GRAFANA_DS_PROMETHEUS, GRAFANA_DS_TEMPO, GRAFANA_DS_LOKI
                 os uids dos datasources; por padrão os do Grafana Cloud (grafanacloud-prom, -traces,
                 -logs) se existirem, senão os do local (prometheus, tempo, loki)
"""

import json
import os
import pathlib
import sys
import urllib.error
import urllib.parse
import urllib.request

import yaml

AQUI = pathlib.Path(__file__).resolve().parent
DASHBOARDS = AQUI.parent.parent / "infra" / "grafana" / "dashboards"
PASTA_UID = "compra-facil"
# o uid local de cada datasource (o dos dashboards) e o do Grafana Cloud
DATASOURCES = {"prometheus": "grafanacloud-prom", "tempo": "grafanacloud-traces", "loki": "grafanacloud-logs"}


class Grafana:
    def __init__(self, url, token):
        self.url = url.rstrip("/")
        self.headers = {"Content-Type": "application/json", "Accept": "application/json"}
        if token:
            self.headers["Authorization"] = f"Bearer {token}"

    def __call__(self, method, path, body=None, ok=(200,)):
        req = urllib.request.Request(self.url + path, method=method, headers=self.headers,
                                     data=None if body is None else json.dumps(body).encode())
        try:
            with urllib.request.urlopen(req, timeout=30) as resp:
                status, raw = resp.status, resp.read()
        except urllib.error.HTTPError as e:
            status, raw = e.code, e.read()
        if status not in ok:
            sys.exit(f"{method} {path}: HTTP {status} {raw.decode(errors='replace')[:500]}")
        return status, (json.loads(raw) if raw else None)


def datasources(api):
    """O uid de cada datasource no destino: a variável, o do Grafana Cloud ou o local, nessa ordem."""
    def existe(uid):
        status, _ = api("GET", f"/api/datasources/uid/{urllib.parse.quote(uid)}", ok=(200, 404))
        return status == 200

    uids = {}
    for local, nuvem in DATASOURCES.items():
        escolhido = os.environ.get(f"GRAFANA_DS_{local.upper()}") or (nuvem if existe(nuvem) else local)
        if not existe(escolhido):
            sys.exit(f"datasource {local}: não existe um com o uid {escolhido!r} (GRAFANA_DS_{local.upper()})")
        uids[local] = escolhido
    return uids


def troca_datasources(node, uids):
    if isinstance(node, dict):
        if node.get("uid") in uids and node.get("type") in uids:
            node = {**node, "uid": uids[node["uid"]]}
        return {k: troca_datasources(v, uids) for k, v in node.items()}
    if isinstance(node, list):
        return [troca_datasources(v, uids) for v in node]
    return node


def pasta(api, titulo):
    status, _ = api("GET", f"/api/folders/{PASTA_UID}", ok=(200, 404))
    if status == 404:
        api("POST", "/api/folders", {"uid": PASTA_UID, "title": titulo})
    else:
        api("PUT", f"/api/folders/{PASTA_UID}", {"title": titulo, "overwrite": True})


def publica_dashboards(api, uids):
    for arquivo in sorted(DASHBOARDS.glob("*.json")):
        dash = troca_datasources(json.loads(arquivo.read_text()), uids)
        dash.pop("id", None)
        api("POST", "/api/dashboards/db", {"dashboard": dash, "folderUid": PASTA_UID, "overwrite": True,
                                           "message": "publicar.py"})
        print(f"dashboard {dash['uid']}")


def publica_contato(api, nome, emails):
    contato = {"uid": nome, "name": nome, "type": "email", "disableResolveMessage": False,
               "settings": {"addresses": emails, "singleEmail": True}}
    _, existentes = api("GET", "/api/v1/provisioning/contact-points")
    if any(c.get("uid") == nome for c in existentes):
        api("PUT", f"/api/v1/provisioning/contact-points/{nome}", contato, ok=(200, 202))
    else:
        api("POST", "/api/v1/provisioning/contact-points", contato, ok=(200, 201, 202))
    print(f"contato {nome}: {emails}")


def segundos(duracao):
    unidades = {"s": 1, "m": 60, "h": 3600}
    return int(duracao[:-1]) * unidades[duracao[-1]]


def regra(r, cfg, prometheus):
    return {
        "uid": r["uid"],
        "title": r["titulo"],
        "folderUID": PASTA_UID,
        "ruleGroup": cfg["grupo"],
        "condition": "B",
        "data": [
            {"refId": "A", "datasourceUid": prometheus, "relativeTimeRange": {"from": 600, "to": 0},
             "model": {"refId": "A", "expr": r["expr"].strip(), "instant": True, "range": False}},
            {"refId": "B", "datasourceUid": "__expr__",
             "model": {"refId": "B", "type": "threshold", "expression": "A",
                       "conditions": [{"evaluator": {"type": "gt", "params": [r.get("limiar", 0)]}}]}},
        ],
        "noDataState": r.get("sem_dados", "OK"),
        "execErrState": "Error",
        "for": r["para"],
        "labels": {"severidade": r["severidade"]},
        "annotations": {"summary": r["resumo"], "description": r["descricao"].strip()},
        # vai direto para o contato, sem mexer na árvore de notificação (que pode ser de outros projetos)
        "notification_settings": {"receiver": cfg["contato"]["nome"],
                                  "group_by": ["alertname", "grafana_folder"],
                                  "repeat_interval": "4h"},
    }


def publica_regras(api, cfg, prometheus):
    grupo = {"title": cfg["grupo"], "folderUid": PASTA_UID, "interval": segundos(cfg["intervalo"]),
             "rules": [regra(r, cfg, prometheus) for r in cfg["regras"]]}
    api("PUT", f"/api/v1/provisioning/folder/{PASTA_UID}/rule-groups/{urllib.parse.quote(cfg['grupo'])}",
        grupo)
    for r in cfg["regras"]:
        print(f"alerta {r['uid']}: {r['titulo']}")


def main():
    faltam = [v for v in ("GRAFANA_URL", "ALERT_EMAIL") if not os.environ.get(v)]
    if faltam:
        sys.exit(f"faltam as variáveis {', '.join(faltam)} (ver deploy/grafana/README.md)")
    api = Grafana(os.environ["GRAFANA_URL"], os.environ.get("GRAFANA_TOKEN", ""))
    cfg = yaml.safe_load((AQUI / "alertas.yaml").read_text())

    uids = datasources(api)
    print("datasources: " + ", ".join(f"{k}={v}" for k, v in uids.items()))
    pasta(api, cfg["pasta"])
    publica_dashboards(api, uids)
    publica_contato(api, cfg["contato"]["nome"], os.environ["ALERT_EMAIL"])
    publica_regras(api, cfg, uids["prometheus"])


if __name__ == "__main__":
    main()
