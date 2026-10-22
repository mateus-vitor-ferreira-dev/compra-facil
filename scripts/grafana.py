#!/usr/bin/env python3
"""Atalhos para o Grafana local (#126): abre o navegador já no lugar certo.

  python3 scripts/grafana.py                 # dashboard Início
  python3 scripts/grafana.py trace <id>      # o trace (X-Trace-Id / trace_id de uma resposta)
  python3 scripts/grafana.py logs <serviço>  # logs do serviço no Loki (Explore)

Sem navegador (ex.: SSH), só imprime a URL. GRAFANA_URL muda o endereço (padrão http://127.0.0.1:3001).
"""

import json
import os
import re
import sys
import urllib.parse
import webbrowser

GRAFANA = os.environ.get("GRAFANA_URL", "http://127.0.0.1:3001")
SERVICES = ("catalog", "orders", "inventory", "payments", "notifications", "gateway")


def explore(datasource, query, extra=None, since="now-1h"):
    q = {"refId": "A", "datasource": {"type": datasource, "uid": datasource}, **(extra or {}), **query}
    panes = {"a": {"datasource": datasource, "queries": [q], "range": {"from": since, "to": "now"}}}
    return f"{GRAFANA}/explore?schemaVersion=1&orgId=1&panes={urllib.parse.quote(json.dumps(panes))}"


def main(args):
    if not args:
        url = f"{GRAFANA}/d/compra-facil-inicio"
    elif args[0] == "trace" and len(args) == 2:
        trace_id = args[1].strip().lower()
        if not re.fullmatch(r"[0-9a-f]{32}", trace_id):
            sys.exit(f"trace_id inválido: {args[1]!r} (32 caracteres hex, do header X-Trace-Id)")
        url = explore("tempo", {"queryType": "traceql", "query": trace_id}, since="now-24h")
    elif args[0] == "logs" and len(args) == 2:
        if args[1] not in SERVICES:
            sys.exit(f"serviço desconhecido: {args[1]!r} ({', '.join(SERVICES)})")
        url = explore("loki", {"expr": f'{{service_name="{args[1]}"}}'})
    else:
        sys.exit(__doc__)
    print(url)
    webbrowser.open(url)


if __name__ == "__main__":
    main(sys.argv[1:])
