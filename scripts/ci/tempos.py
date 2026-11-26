#!/usr/bin/env python3
"""Onde vai o tempo do CI (#415): por job, a fila (criado → começou) e a execução (começou → terminou), e os
passos mais lentos. É a régua do antes e do depois de cada mudança no CI.

    scripts/ci/tempos.py --desde 2026-11-11 [--ate 2026-11-16] [--branch develop] [--evento pull_request]
    scripts/ci/tempos.py --run 43885270166          # um run só, passo a passo

Usa o `gh` já autenticado. Só lê.
"""
import argparse
import json
import statistics
import subprocess
import sys
from collections import defaultdict
from datetime import datetime

REPO = "mateus-vitor-ferreira-dev/embrulhado"


def gh(path: str) -> dict:
    out = subprocess.run(["gh", "api", "-H", "Accept: application/vnd.github+json", path],
                         capture_output=True, text=True, check=True).stdout
    return json.loads(out)


def quando(s: str | None) -> datetime | None:
    return datetime.fromisoformat(s.replace("Z", "+00:00")) if s else None


def minutos(a: datetime | None, b: datetime | None) -> float | None:
    return round((b - a).total_seconds() / 60, 1) if a and b else None


def jobs_do_run(run_id: int) -> list[dict]:
    jobs, pagina = [], 1
    while True:
        r = gh(f"repos/{REPO}/actions/runs/{run_id}/jobs?per_page=100&page={pagina}")
        jobs += r["jobs"]
        if len(jobs) >= r["total_count"] or not r["jobs"]:
            return jobs
        pagina += 1


def runs(desde: str, ate: str | None, branch: str | None, evento: str | None) -> list[dict]:
    filtro = f"created={desde}..{ate}" if ate else f"created=>={desde}"
    q = f"repos/{REPO}/actions/runs?per_page=100&{filtro}"
    if branch:
        q += f"&branch={branch}"
    if evento:
        q += f"&event={evento}"
    saida, pagina = [], 1
    while True:
        r = gh(f"{q}&page={pagina}")
        saida += r["workflow_runs"]
        if len(saida) >= r["total_count"] or not r["workflow_runs"]:
            return saida
        pagina += 1


def medir(job: dict) -> dict:
    criado, inicio, fim = quando(job.get("created_at")), quando(job.get("started_at")), quando(job.get("completed_at"))
    passos = [(p["name"], minutos(quando(p.get("started_at")), quando(p.get("completed_at"))))
              for p in job.get("steps") or []]
    lentos = sorted((p for p in passos if p[1] is not None), key=lambda p: -p[1])[:3]
    sem_passo = not any(p[1] for p in passos)
    return {
        "job": job["name"], "conclusao": job.get("conclusion"), "runner": job.get("runner_name"),
        "fila": minutos(criado, inicio), "execucao": minutos(inicio, fim), "lentos": lentos, "sem_passo": sem_passo,
    }


def p90(xs: list[float]) -> float:
    xs = sorted(xs)
    return xs[min(len(xs) - 1, int(round(0.9 * (len(xs) - 1))))]


def um_run(run_id: int) -> None:
    for j in map(medir, jobs_do_run(run_id)):
        lentos = "; ".join(f"{n} {m}" for n, m in j["lentos"])
        print(f"{j['job'][:50]:50} {j['conclusao'] or '-':10} fila {j['fila']!s:>6} min  exec {j['execucao']!s:>6} min  {lentos}")


def resumo(args) -> None:
    por_job: dict[str, list[dict]] = defaultdict(list)
    lista = runs(args.desde, args.ate, args.branch, args.evento)
    print(f"{len(lista)} runs", file=sys.stderr)
    for run in lista:
        for j in map(medir, jobs_do_run(run["id"])):
            if j["conclusao"] == "skipped":
                continue
            j["run"] = run["id"]
            por_job[j["job"]].append(j)

    print(f"{'job':50} {'n':>3} {'fila med':>9} {'fila p90':>9} {'exec med':>9} {'exec p90':>9}  presos/cancelados")
    linhas = []
    for nome, js in por_job.items():
        fila = [j["fila"] for j in js if j["fila"] is not None]
        exe = [j["execucao"] for j in js if j["execucao"] is not None and not j["sem_passo"]]
        presos = sum(1 for j in js if j["conclusao"] == "cancelled" or (j["execucao"] or 0) > 60)
        linhas.append((p90(fila) if fila else 0, nome, len(js), fila, exe, presos))
    for _, nome, n, fila, exe, presos in sorted(linhas, reverse=True):
        med = lambda xs: f"{statistics.median(xs):9.1f}" if xs else f"{'-':>9}"  # noqa: E731
        q90 = lambda xs: f"{p90(xs):9.1f}" if xs else f"{'-':>9}"  # noqa: E731
        print(f"{nome[:50]:50} {n:3} {med(fila)} {q90(fila)} {med(exe)} {q90(exe)}  {presos}")

    passos: dict[str, list[float]] = defaultdict(list)
    for js in por_job.values():
        for j in js:
            for nome, m in j["lentos"]:
                passos[nome].append(m)
    print("\npassos mais lentos (soma dos minutos no período)")
    for nome, ms in sorted(passos.items(), key=lambda kv: -sum(kv[1]))[:10]:
        print(f"  {sum(ms):7.1f} min  em {len(ms):3} jobs  {nome}")


def main() -> None:
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--desde", help="data inicial (AAAA-MM-DD)")
    p.add_argument("--ate", help="data final (AAAA-MM-DD)")
    p.add_argument("--branch")
    p.add_argument("--evento", help="push, pull_request...")
    p.add_argument("--run", type=int, help="um run só, passo a passo")
    args = p.parse_args()
    if args.run:
        um_run(args.run)
    elif args.desde:
        resumo(args)
    else:
        p.error("use --desde ou --run")


if __name__ == "__main__":
    main()
