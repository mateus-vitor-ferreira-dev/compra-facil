#!/usr/bin/env bash
# Lista os serviços do compose que o recurso do Coolify usa, um por linha. O passo "Responde de fora" do
# deploy.yml só confere esses: o compose.yaml (produção) não tem o fulfillment, e o compose.demo.yaml tem.
#
# O compose vem do "Docker Compose Location" do recurso. Se o Coolify não disser qual é, a lista fica com
# os serviços que os dois compose têm, com um aviso: a checagem perde só o que é próprio de um deles.
#
# Variáveis:
#   COOLIFY_URL, COOLIFY_TOKEN, COOLIFY_APP_UUID   o painel, o token da API e o recurso da API
set -euo pipefail

: "${COOLIFY_URL:?}" "${COOLIFY_TOKEN:?}" "${COOLIFY_APP_UUID:?}"
dir=$(dirname "$0")

# os serviços de primeiro nível (dois espaços de recuo dentro de services:)
servicos() {
  awk '/^services:/ { s = 1; next } /^[^ #]/ { s = 0 } s && /^  [a-z0-9_-]+:/ { sub(/:.*/, ""); sub(/^  /, ""); print }' "$1"
}

local_do_compose=$(curl -sS --max-time 30 -H "Authorization: Bearer $COOLIFY_TOKEN" \
  "${COOLIFY_URL%/}/api/v1/applications/$COOLIFY_APP_UUID" | jq -r '.docker_compose_location // empty' 2>/dev/null || true)
arquivo="$dir/$(basename "${local_do_compose:-nenhum}")"

if [[ -n $local_do_compose && -f $arquivo ]]; then
  servicos "$arquivo"
else
  echo "::warning title=Compose desconhecido::O Coolify não disse qual compose o recurso usa (\"$local_do_compose\"). Confiro só os serviços que os dois têm." >&2
  LC_ALL=C comm -12 <(servicos "$dir/compose.yaml" | LC_ALL=C sort) <(servicos "$dir/compose.demo.yaml" | LC_ALL=C sort)
fi
