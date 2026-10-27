#!/usr/bin/env bash
# Põe o endpoint S3 compatível do Object Storage da Oracle no lugar do de exemplo (objectstorage.example)
# no GitOps de produção, uma vez, depois do primeiro apply do infra/oci (#157).
#   scripts/deploy/definir-object-storage.sh "$(tofu -chdir=infra/oci output -raw s3_endpoint)"
set -euo pipefail
endpoint=${1:?uso: $0 <endpoint, ex.: https://<namespace>.compat.objectstorage.sa-saopaulo-1.oraclecloud.com>}
[[ $endpoint =~ ^https://[a-z0-9.-]+$ ]] || { echo "endpoint inválido: $endpoint" >&2; exit 1; }
root=$(git rev-parse --show-toplevel)
mapfile -t files < <(grep -rl 'https://objectstorage\.example' "$root/infra/k8s/producao")
[[ ${#files[@]} -gt 0 ]] || { echo "nenhum objectstorage.example para trocar (já definido?)"; exit 0; }
sed -i "s#https://objectstorage\.example#$endpoint#g" "${files[@]}"
printf 'trocado em %d arquivos:\n' "${#files[@]}"
printf '  %s\n' "${files[@]#"$root/"}"
