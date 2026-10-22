#!/usr/bin/env python3
"""Autonomia de entrega por serviço (#91), critério observável 2 do parecer.

Deploy = imagem publicada no GHCR pelo pipeline do serviço (job "imagem no GHCR" com sucesso) num
push para develop ou main, ou seja, o merge de um PR. A partir do histórico do GitHub Actions (gh):

- deploys por serviço e por semana;
- lead time: do primeiro commit do PR até a imagem publicada (mediana e máximo);
- deploys sozinhos: quantos merges publicaram só aquele serviço, sem arrastar os outros.

Uso: python3 scripts/delivery_metrics.py [--out docs/resultados-entrega.md]
Precisa do gh autenticado no repositório.
"""

import argparse
import json
import statistics
import subprocess
from collections import defaultdict
from datetime import datetime, timezone
from pathlib import Path

SERVICES = ["catalog", "orders", "inventory", "payments", "notifications"]
IMAGE_JOB = "imagem no GHCR"
BRANCHES = ("develop", "main")


def gh(*args):
    out = subprocess.run(["gh", *args], capture_output=True, text=True, check=True).stdout
    return json.loads(out) if out.strip() else []


def ts(text):
    return datetime.fromisoformat(text.replace("Z", "+00:00"))


def deploys_of(service):
    """(sha, publicado_em) de cada push em develop/main que publicou a imagem do serviço."""
    found = []
    for branch in BRANCHES:
        runs = gh("run", "list", "--workflow", f"{service}.yml", "--branch", branch, "--event", "push",
                  "--limit", "200", "--json", "databaseId,headSha")
        for run in runs:
            jobs = gh("run", "view", str(run["databaseId"]), "--json", "jobs")["jobs"]
            image = next((j for j in jobs if j["name"].endswith(IMAGE_JOB)), None)
            if image and image["conclusion"] == "success" and image.get("completedAt"):
                found.append((run["headSha"], ts(image["completedAt"])))
    return found


def merged_prs():
    """PR por sha do merge (o primeiro commit, início do lead time, vem sob demanda: first_commit)."""
    prs = gh("pr", "list", "--state", "merged", "--limit", "200", "--json", "number,title,mergeCommit,mergedAt")
    return {pr["mergeCommit"]["oid"]: pr for pr in prs if pr.get("mergeCommit")}


def first_commit(pr):
    """Data do primeiro commit do PR (API REST: o GraphQL do pr list estoura o limite de nós)."""
    if "first_commit" not in pr:
        dates = gh("api", f"repos/{{owner}}/{{repo}}/pulls/{pr['number']}/commits?per_page=100",
                   "--jq", "[.[].commit.author.date]")
        pr["first_commit"] = min((ts(d) for d in dates), default=ts(pr["mergedAt"]))
    return pr["first_commit"]


def hours(delta):
    return delta.total_seconds() / 3600


def fmt_h(h):
    return f"{h * 60:.0f} min" if h < 1 else f"{h:.1f} h"


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--out", default="docs/resultados-entrega.md")
    args = p.parse_args()

    prs = merged_prs()
    per_service = {s: deploys_of(s) for s in SERVICES}

    # quais serviços cada merge publicou
    services_by_sha = defaultdict(set)
    for service, deploys in per_service.items():
        for sha, _ in deploys:
            services_by_sha[sha].add(service)

    rows, details = [], []
    all_times = [t for d in per_service.values() for _, t in d]
    if not all_times:
        raise SystemExit("nenhum deploy encontrado no histórico do Actions")
    start, end = min(all_times), max(all_times)
    # período menor que uma semana conta como uma semana (não extrapola minutos para semanas)
    weeks = max(hours(end - start) / (24 * 7), 1)

    for service in SERVICES:
        deploys = per_service[service]
        leads = []
        alone = 0
        for sha, published in deploys:
            pr = prs.get(sha)
            if pr:
                leads.append(hours(published - first_commit(pr)))
            if services_by_sha[sha] == {service}:
                alone += 1
        rows.append({
            "service": service,
            "deploys": len(deploys),
            "per_week": len(deploys) / weeks,
            "alone": alone,
            "lead_median_h": statistics.median(leads) if leads else None,
            "lead_max_h": max(leads) if leads else None,
        })

    for sha, services in sorted(services_by_sha.items(), key=lambda kv: min(t for s in kv[1] for x, t in per_service[s] if x == sha)):
        pr = prs.get(sha)
        details.append((f"#{pr['number']} {pr['title']}" if pr else sha[:7], sorted(services)))

    table = ["| Serviço | Deploys | Por semana | Sozinho (sem outros serviços) | Lead time (mediana) | Lead time (máx.) |",
             "|---|---|---|---|---|---|"]
    for r in rows:
        table.append(f"| {r['service']} | {r['deploys']} | {r['per_week']:.1f} | {r['alone']} de {r['deploys']} | "
                     f"{fmt_h(r['lead_median_h']) if r['lead_median_h'] is not None else '—'} | "
                     f"{fmt_h(r['lead_max_h']) if r['lead_max_h'] is not None else '—'} |")
    merges = ["| Merge | Serviços publicados |", "|---|---|"]
    merges += [f"| {name} | {', '.join(s)} |" for name, s in details]

    together = sum(1 for _, s in details if len(s) > 1)
    when = datetime.now(timezone.utc).strftime("%Y-%m-%d %H:%M UTC")
    text = f"""# Resultados de entrega: autonomia por serviço

Critério observável 2 do parecer (#91). Gerado por `python3 scripts/delivery_metrics.py` em {when}, a partir
do histórico do GitHub Actions.

## Como foi medido

- **Deploy:** imagem do serviço publicada no GHCR (job "{IMAGE_JOB}") por um push em `develop` ou `main`, que
  é o merge de um PR. Cada serviço tem o próprio pipeline, disparado só pelos caminhos dele
  (`.github/workflows/<serviço>.yml`).
- **Lead time:** do primeiro commit do PR até a imagem publicada.
- **Deploy sozinho:** o merge publicou só esse serviço. É a medida direta de "um time entrega sem
  coordenar com o outro". Mudanças na biblioteca comum .NET (`libs/dotnet`) publicam os quatro serviços
  .NET juntos, e isso aparece aqui.
- **Período:** de {start:%Y-%m-%d %H:%M} a {end:%Y-%m-%d %H:%M} UTC, com {sum(r['deploys'] for r in rows)} deploys. O
  histórico é curto (o CI por serviço existe desde o PR #122): com menos de uma semana de dados, "por
  semana" é o total do período.

## Por serviço

{chr(10).join(table)}

## Por merge

{chr(10).join(merges)}

{together} de {len(details)} merges publicaram mais de um serviço.

## Por que um merge publica vários serviços

O pipeline de cada serviço dispara pelos caminhos que o afetam. Três tipos de mudança publicam mais de um:

- **Código comum dos serviços .NET** (`libs/dotnet`, `Directory.Build.props`, `Directory.Packages.props`,
  `services/dotnet.Dockerfile`): os quatro serviços de compras são republicados juntos. É o preço da biblioteca
  compartilhada (outbox, Kafka, OpenTelemetry). Uma versão publicada como pacote, que cada serviço atualiza
  quando quiser, trocaria esse acoplamento de deploy por um de versão.
- **Contratos:** quem publica ou consome um contrato é republicado quando ele muda. Até o #91, o pipeline do
  catalog disparava com qualquer arquivo em `contracts/`; agora ele só olha os contratos que serve.
- **Trabalho de plataforma** (CI, observabilidade): atravessa todos os serviços de propósito.

Mudança só dentro de um serviço publica só ele. O #123, por exemplo, publicou apenas o catalog.
"""
    Path(args.out).write_text(text)
    print(text)


if __name__ == "__main__":
    main()
