#!/usr/bin/env bash
# Gera os SealedSecrets da produção a partir de um arquivo local (fora do git) com os valores, usando a
# chave pública do cluster: o resultado pode ir para o git, e só o controller do cluster o abre.
#   cp scripts/deploy/segredos.env.example ~/.compra-facil/segredos.env   # e preencha
#   KUBECONFIG=~/.kube/compra-facil.yaml scripts/deploy/selar-segredos.sh ~/.compra-facil/segredos.env
set -euo pipefail
env_file=${1:?uso: $0 <arquivo .env com os segredos>}
root=$(git rev-parse --show-toplevel)
out="$root/infra/k8s/producao/platform/base/secrets"
cert=$(mktemp)
trap 'rm -f "$cert"' EXIT
kubeseal --controller-namespace kube-system --controller-name sealed-secrets-controller --fetch-cert > "$cert"

# shellcheck disable=SC1090
set -a; source "$env_file"; set +a

# seal <nome do Secret> <tipo> <chave=VARIÁVEL>...
seal() {
  local name=$1 type=$2; shift 2
  local args=()
  for pair in "$@"; do
    local key=${pair%%=*} var=${pair#*=}
    [[ -n ${!var:-} ]] || { echo "faltou $var em $env_file (Secret $name)" >&2; exit 1; }
    args+=(--from-literal="$key=${!var}")
  done
  kubectl create secret generic "$name" --namespace compra-facil --type "$type" "${args[@]}" \
    --dry-run=client -o yaml \
    | kubeseal --cert "$cert" --format yaml > "$out/$name.yaml"
  echo "selado: ${out#"$root/"}/$name.yaml"
}

seal bff-gateway-key Opaque key=BFF_GATEWAY_KEY

# imagens privadas no GHCR: o segredo de pull (dispensável se os pacotes forem públicos)
if [[ -n ${GHCR_TOKEN:-} ]]; then
  kubectl create secret docker-registry ghcr-pull --namespace compra-facil \
    --docker-server=ghcr.io --docker-username="$GHCR_USER" --docker-password="$GHCR_TOKEN" \
    --dry-run=client -o yaml | kubeseal --cert "$cert" --format yaml > "$out/ghcr-pull.yaml"
  echo "selado: ${out#"$root/"}/ghcr-pull.yaml"
fi

# a lista do kustomization: todos os selados da pasta
list=$(mktemp)
{
  echo "# Gerado por scripts/deploy/selar-segredos.sh: os SealedSecrets da produção."
  echo "apiVersion: kustomize.config.k8s.io/v1beta1"
  echo "kind: Kustomization"
  echo "resources:"
  for f in "$out"/*.yaml; do
    f=$(basename "$f")
    [[ $f == kustomization.yaml ]] || echo "  - $f"
  done
} > "$list"
mv "$list" "$out/kustomization.yaml"
