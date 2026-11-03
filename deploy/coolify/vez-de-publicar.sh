#!/usr/bin/env bash
# Decide se este disparo do deploy.yml publica a API. Cada pipeline de imagem que termina na main dispara um
# deploy, e cada publicação recria os contêineres: um merge que mexe em vários serviços derrubava a API uma vez
# por pipeline. Só publica o disparo do último pipeline do commit a terminar, com todos verdes.
#
# - algum pipeline do commit ainda roda: não publica (o último publica);
# - algum terminou sem sucesso: falha, sem publicar (a API do commit não vai ao ar pela metade);
# - todos verdes: publica só se este disparo veio do último a terminar. Dois que terminam juntos não publicam
#   os dois: o desempate é pelo horário de fim e, nele, pelo id.
#
# Escreve publish=true|false em GITHUB_OUTPUT (ou na saída, fora do Actions).
#
# Variáveis:
#   GITHUB_REPOSITORY   o repositório
#   HEAD_SHA            o commit do pipeline que disparou (github.event.workflow_run.head_sha)
#   RUN_ID              o run desse pipeline (github.event.workflow_run.id)
#   GH_TOKEN            com a permissão actions: read
set -euo pipefail

: "${GITHUB_REPOSITORY:?}" "${HEAD_SHA:?}" "${RUN_ID:?}"
OUT=${GITHUB_OUTPUT:-/dev/stdout}

# os mesmos de workflow_run em deploy.yml (e de esperar-api.sh)
PIPELINES='["catalog","identity","orders","inventory","payments","notifications","fulfillment","backup","stack-images"]'

pipelines=$(gh api "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$HEAD_SHA&event=push&per_page=100" |
  jq --argjson nomes "$PIPELINES" '[.workflow_runs[] | select(.name as $n | $nomes | index($n))]')

# a lista vazia é a API do GitHub atrasada: publicar é melhor que nunca publicar
if (( $(jq length <<<"$pipelines") == 0 )); then
  echo "::warning title=Pipelines não encontrados::A API do GitHub não listou os pipelines de $HEAD_SHA. Publico."
  echo "publish=true" >> "$OUT"
  exit 0
fi

abertos=$(jq -r '[.[] | select(.status != "completed") | .name] | join(", ")' <<<"$pipelines")
if [[ -n $abertos ]]; then
  echo "Ainda rodam: $abertos. O último a terminar publica."
  echo "publish=false" >> "$OUT"
  exit 0
fi

ruins=$(jq -r '[.[] | select(.conclusion != "success") | "\(.name) (\(.conclusion))"] | join(", ")' <<<"$pipelines")
if [[ -n $ruins ]]; then
  echo "::error title=A API não foi publicada::O pipeline não passou: $ruins. Nada deste commit vai ao ar."
  exit 1
fi

ultimo=$(jq -r 'sort_by(.updated_at, .id) | last | .id' <<<"$pipelines")
if [[ $ultimo == "$RUN_ID" ]]; then
  echo "Todos os $(jq length <<<"$pipelines") pipelines do commit passaram, e este é o último: publica."
  echo "publish=true" >> "$OUT"
else
  echo "Todos passaram, mas o último a terminar foi o run $ultimo: ele publica."
  echo "publish=false" >> "$OUT"
fi
