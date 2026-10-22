#!/usr/bin/env python3
"""Experimento da escala independente (#90), critério observável 1 do parecer.

Roda o cenário de campanha (k6/campanha.js) duas vezes, com os mesmos limites de recurso por réplica
(infra/compose/compose.experiment.yaml):

  A: uma réplica de cada serviço;
  B: o catalog com N réplicas, o resto com uma.

Durante o pico, amostra o CPU e a memória de cada container (docker stats). No fim, grava os
números brutos em load-tests/results/ e o relatório em docs/resultados-carga.md.

Uso (da raiz, com `task seed` já feito): task experiment -- --replicas 3 --peak-rate 1000
"""

import argparse
import json
import os
import re
import subprocess
import threading
import time
from datetime import datetime, timezone
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
RESULTS = ROOT / "load-tests" / "results"
REPORT = ROOT / "docs" / "resultados-carga.md"
COMPOSE = ["docker", "compose", "-f", "compose.yaml", "-f", "infra/compose/compose.experiment.yaml"]
APP_SERVICES = ("catalog", "orders", "inventory", "payments", "notifications")
WARMUP_S = 60  # 30 s de aquecimento + 30 s de rampa no campanha.js
UNITS = {"B": 1, "KiB": 1024, "MiB": 1024**2, "GiB": 1024**3, "kB": 1000, "MB": 1000**2, "GB": 1000**3}


def run(cmd, env, **kw):
    print("$", " ".join(cmd), flush=True)
    return subprocess.run(cmd, cwd=ROOT, env=env, check=True, **kw)


def parse_mem(text):
    m = re.match(r"([\d.]+)\s*([A-Za-z]+)", text.split("/")[0].strip())
    return float(m.group(1)) * UNITS[m.group(2)] if m else 0.0


def service_of(container):
    # comprafacil-catalog-2 -> catalog
    m = re.match(r"comprafacil-(.+)-\d+$", container)
    return m.group(1) if m else container


class Sampler(threading.Thread):
    """docker stats a cada ~5 s; guarda (instante, serviço, cpu em núcleos, memória em bytes)."""

    def __init__(self):
        super().__init__(daemon=True)
        self.samples = []
        self.stop = threading.Event()

    def run(self):
        while not self.stop.is_set():
            out = subprocess.run(
                ["docker", "stats", "--no-stream", "--format", "{{.Name}};{{.CPUPerc}};{{.MemUsage}}"],
                capture_output=True, text=True, check=False).stdout
            now = time.time()
            for line in out.splitlines():
                name, cpu, mem = line.split(";")
                if name.startswith("comprafacil-"):
                    self.samples.append((now, service_of(name), float(cpu.rstrip("%") or 0) / 100, parse_mem(mem)))
            self.stop.wait(5)


def summarize(samples, start, end):
    """Média, por amostra, da soma de CPU/memória: dos serviços da aplicação, do catalog e do total."""
    by_time = {}
    for t, svc, cpu, mem in samples:
        if start <= t <= end:
            by_time.setdefault(round(t), []).append((svc, cpu, mem))
    rows = list(by_time.values())
    if not rows:
        return {}

    def avg(pred, idx):
        return sum(sum(x[idx] for x in r if pred(x[0])) for r in rows) / len(rows)

    return {
        "samples": len(rows),
        "cpu_app": avg(lambda s: s in APP_SERVICES, 1),
        "cpu_catalog": avg(lambda s: s == "catalog", 1),
        "cpu_total": avg(lambda s: True, 1),
        "mem_app": avg(lambda s: s in APP_SERVICES, 2),
        "mem_catalog": avg(lambda s: s == "catalog", 2),
        "mem_total": avg(lambda s: True, 2),
    }


def metric(summary, name, stat):
    return summary["metrics"].get(name, {}).get(stat)


def round_(label, replicas, args, env):
    env = {**env, "CATALOG_REPLICAS": str(replicas), "CATALOG_CPUS": args.catalog_cpus}
    run([*COMPOSE, "up", "-d", "--wait"], env)
    time.sleep(args.settle)  # cache do catalog e conexões estabilizam

    sampler = Sampler()
    sampler.start()
    started = time.time()
    export = f"/results/{label}.json"
    # o k6 roda com o usuário daqui: a imagem usa outro, que não grava em load-tests/results
    k6 = [*COMPOSE, "run", "--rm", "--user", f"{os.getuid()}:{os.getgid()}", "-v", f"{RESULTS}:/results",
          "k6", "run", "--quiet", "-o", "experimental-prometheus-rw", "--tag", f"round={label}",
          "--summary-export", export, "-e", f"PEAK_RATE={args.peak_rate}", "-e", f"BASE_RATE={args.base_rate}",
          "-e", f"PEAK={args.peak}", "/scripts/campanha.js"]
    print("$", " ".join(k6), flush=True)
    subprocess.run(k6, cwd=ROOT, env=env, check=False)  # threshold violado não interrompe o experimento
    sampler.stop.set()
    sampler.join()

    summary = json.loads((RESULTS / f"{label}.json").read_text())
    peak_start = started + WARMUP_S
    peak_end = peak_start + parse_duration(args.peak)
    return {
        "label": label,
        "catalog_replicas": replicas,
        "rps": metric(summary, "http_reqs", "rate"),
        "p95_ms": metric(summary, "http_req_duration", "p(95)"),
        "p95_browse_ms": metric(summary, "http_req_duration{scenario:browse}", "p(95)"),
        "p95_buy_ms": metric(summary, "http_req_duration{scenario:buy}", "p(95)"),
        "p99_ms": metric(summary, "http_req_duration", "p(99)"),
        "error_rate": metric(summary, "http_req_failed", "value"),
        "requests": metric(summary, "http_reqs", "count"),
        "dropped": metric(summary, "dropped_iterations", "count") or 0,
        **summarize(sampler.samples, peak_start, peak_end),
    }


def parse_duration(text):
    m = re.fullmatch(r"(\d+)([smh])", text)
    return int(m.group(1)) * {"s": 1, "m": 60, "h": 3600}[m.group(2)]


def fmt(v, spec, scale=1.0):
    return "—" if v is None else format(v * scale, spec)


def report(rounds, args):
    a, b = rounds
    header = ("| Rodada | Réplicas do catalog | Req/s atendidas | Não enviadas (saturação) | p95 (ms) | p95 leitura | p95 compra "
              "| p99 (ms) | Erros | CPU app (núcleos) | CPU catalog | Memória app (MiB) |")
    lines = [header, "|" + "---|" * 12]
    for r in rounds:
        lines.append(" | ".join([
            f"| {r['label']}", str(r["catalog_replicas"]), fmt(r["rps"], ".0f"), f"{r.get('dropped', 0):,.0f}".replace(",", "."),
            fmt(r["p95_ms"], ".0f"),
            fmt(r["p95_browse_ms"], ".0f"), fmt(r["p95_buy_ms"], ".0f"), fmt(r["p99_ms"], ".0f"),
            fmt(r["error_rate"], ".3%"), fmt(r.get("cpu_app"), ".2f"), fmt(r.get("cpu_catalog"), ".2f"),
            fmt(r.get("mem_app"), ".0f", 1 / 1024**2)]) + " |")

    def bar(title, key, unit, scale=1.0):
        vals = [round((r.get(key) or 0) * scale, 2) for r in rounds]
        top = max(vals) * 1.2 or 1
        return "\n".join([
            "```mermaid", "xychart-beta", f'    title "{title}"',
            f'    x-axis ["A: 1 réplica", "B: {b["catalog_replicas"]} réplicas"]',
            f'    y-axis "{unit}" 0 --> {top:.0f}' if top > 5 else f'    y-axis "{unit}" 0 --> {top:.2f}',
            f"    bar [{', '.join(str(v) for v in vals)}]", "```"])

    when = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    text = f"""# Resultados de carga: escala independente do catálogo

Critério observável 1 do parecer (#90). Gerado por `task experiment` (`load-tests/experiment.py`) em {when}.

## Como foi medido

- **Carga:** `load-tests/k6/campanha.js`. São 90% de leitura do catálogo (55% detalhe de produto, quase todo em
  produtos da campanha; 25% listagem por categoria; buscas, categorias e campanhas) e 10% de compras
  (`POST /v1/orders`, que valida e precifica no catalog por gRPC e inicia a SAGA). A rampa vai de
  {args.base_rate} a **{args.peak_rate} req/s**, e o pico dura **{args.peak}**.
- **Recursos por réplica** (`infra/compose/compose.experiment.yaml`): catalog com **{args.catalog_cpus} CPU** e
  256 MiB; os outros serviços com 1 CPU e 512 MiB. Postgres, Redis e Redpanda não têm limite.
- **Rodadas:** A, com uma réplica de cada serviço; B, com **{b['catalog_replicas']} réplicas do catalog** e o resto igual.
- **Medições:** latência e erros vêm do resumo do k6, considerando o tráfego todo, rampa incluída. CPU e
  memória são a média das amostras do `docker stats` durante o pico, somando os containers dos 5 serviços
  ("app") e só os do catalog.
- **Limite do método:** gerador de carga, serviços e infraestrutura dividem a mesma máquina. Os números servem
  para comparar as rodadas entre si, não como capacidade absoluta.

## Resultado

{chr(10).join(lines)}

Metas da #89: p95 < 300 ms e erros < 1%.

{bar("p95 de todas as requisições (ms)", "p95_ms", "ms")}

{bar("Requisições não enviadas por saturação", "dropped", "requisições")}

{bar("CPU gasto pelos serviços no pico (núcleos)", "cpu_app", "núcleos")}

## Leitura

{interpretation(a, b)}

Os números brutos (resumo do k6 e as médias de recurso) estão em `load-tests/results/`.
"""
    REPORT.write_text(text)
    (RESULTS / "rounds.json").write_text(json.dumps(rounds, indent=2))
    print(f"relatório: {REPORT}")


def interpretation(a, b):
    parts = []
    if a["p95_ms"] and b["p95_ms"]:
        parts.append(f"- **Latência:** o p95 foi de {a['p95_ms']:.0f} ms para {b['p95_ms']:.0f} ms "
                     f"({(b['p95_ms'] / a['p95_ms'] - 1):+.0%}).")
    if a["error_rate"] is not None and b["error_rate"] is not None:
        parts.append(f"- **Erros:** {a['error_rate']:.3%} → {b['error_rate']:.3%}.")
    if a.get("dropped") or b.get("dropped"):
        def n(v):
            return f"{v:,.0f}".replace(",", ".")
        parts.append(f"- **Saturação:** com uma réplica, {n(a.get('dropped', 0))} requisições nem chegaram a ser enviadas "
                     f"(o k6 ficou sem usuários virtuais livres, porque os anteriores ainda esperavam resposta); "
                     f"com {b['catalog_replicas']}, foram {n(b.get('dropped', 0))}. Por isso a vazão atendida "
                     f"é maior na rodada B com a mesma carga oferecida.")
    if a.get("cpu_app") and b.get("cpu_app"):
        parts.append(f"- **Custo:** o CPU dos serviços foi de {a['cpu_app']:.2f} para {b['cpu_app']:.2f} núcleos e a memória "
                     f"de {a['mem_app'] / 1024**2:.0f} para {b['mem_app'] / 1024**2:.0f} MiB. Só o catalog ganhou "
                     f"réplicas: orders, inventory, payments e notifications seguiram com uma.")
    return "\n".join(parts)


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--replicas", type=int, default=3, help="réplicas do catalog na rodada B")
    p.add_argument("--peak-rate", type=int, default=1000)
    p.add_argument("--base-rate", type=int, default=50)
    p.add_argument("--peak", default="2m")
    p.add_argument("--catalog-cpus", default="0.2", help="CPU por réplica do catalog")
    p.add_argument("--settle", type=int, default=20, help="segundos de espera depois de subir cada rodada")
    p.add_argument("--report-only", action="store_true",
                   help="só refaz o relatório a partir de load-tests/results (sem rodar a carga)")
    args = p.parse_args()

    RESULTS.mkdir(parents=True, exist_ok=True)
    if args.report_only:
        rounds = json.loads((RESULTS / "rounds.json").read_text())
        for r in rounds:
            summary = json.loads((RESULTS / f"{r['label']}.json").read_text())
            r["dropped"] = metric(summary, "dropped_iterations", "count") or 0
        report(rounds, args)
        return
    env = {**os.environ}
    rounds = [round_("A", 1, args, env), round_("B", args.replicas, args, env)]
    report(rounds, args)
    # volta ao normal: uma réplica, sem limites
    run(["docker", "compose", "up", "-d", "--wait"], env)


if __name__ == "__main__":
    main()
