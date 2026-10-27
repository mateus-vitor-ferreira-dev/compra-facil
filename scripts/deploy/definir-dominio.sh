#!/usr/bin/env bash
# Troca o domínio de exemplo (comprafacil.example) pelo da loja em todo o GitOps de produção, uma vez,
# depois de comprar o domínio (#156). Ex.: scripts/deploy/definir-dominio.sh comprafacil.dev
set -euo pipefail
domain=${1:?uso: $0 <domínio>}
[[ $domain =~ ^[a-z0-9-]+(\.[a-z0-9-]+)+$ ]] || { echo "domínio inválido: $domain" >&2; exit 1; }
root=$(git rev-parse --show-toplevel)
mapfile -t files < <(grep -rl 'comprafacil\.example' "$root/infra/k8s/producao")
[[ ${#files[@]} -gt 0 ]] || { echo "nenhum comprafacil.example para trocar (já definido?)"; exit 0; }
sed -i "s/comprafacil\.example/$domain/g" "${files[@]}"
printf 'trocado em %d arquivos:\n' "${#files[@]}"
printf '  %s\n' "${files[@]#"$root/"}"
