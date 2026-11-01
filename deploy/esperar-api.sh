#!/usr/bin/env bash
# Segura a publicação da loja até a API do mesmo commit estar no ar: a loja nova pode depender de uma rota nova.
# Um commit que não dispara nenhum pipeline de imagem passa direto. Chamado pelo job deploy do web.yml.
#
# Falha, sem publicar, quando um pipeline ou o deploy da API falha: a loja continua na versão anterior.
#
# Variáveis:
#   GITHUB_REPOSITORY, GITHUB_SHA     o repositório e o commit
#   GH_TOKEN                          com a permissão actions: read
#   ESPERA_API_TIMEOUT                segundos até desistir                  (padrão: 1800)
#   ESPERA_API_INTERVALO              segundos entre as consultas            (padrão: 15)
#   ESPERA_API_GATILHO                segundos para o deploy aparecer depois
#                                     do fim dos pipelines                   (padrão: 180)
set -euo pipefail

: "${GITHUB_REPOSITORY:?}" "${GITHUB_SHA:?}"
TIMEOUT=${ESPERA_API_TIMEOUT:-1800}
INTERVALO=${ESPERA_API_INTERVALO:-15}
GATILHO=${ESPERA_API_GATILHO:-180}

# os mesmos de workflow_run em deploy.yml
PIPELINES='["catalog","identity","orders","inventory","payments","notifications","fulfillment","backup","sim-images"]'

erro() {
  echo "::error title=A loja não foi publicada::$1"
  exit 1
}

# uma falha passageira da API do GitHub não derruba a publicação
consulta() {
  local _
  for _ in 1 2 3; do
    gh api "$1" && return 0
    sleep "$INTERVALO"
  done
  echo "::error title=A loja não foi publicada::A API do GitHub não respondeu em $1." >&2
  return 1
}

inicio=$SECONDS
fim_pipelines=""
ultimo=""
while :; do
  (( SECONDS - inicio < TIMEOUT )) || erro "A API deste commit não terminou em ${TIMEOUT}s."

  pipelines=$(consulta "repos/$GITHUB_REPOSITORY/actions/runs?head_sha=$GITHUB_SHA&event=push&per_page=100" |
    jq --argjson nomes "$PIPELINES" '[.workflow_runs[] | select(.name as $n | $nomes | index($n))]')
  total=$(jq 'length' <<<"$pipelines")
  if (( total == 0 )); then
    echo "Este commit não mexe na API: a loja segue."
    exit 0
  fi

  ruim=$(jq -r '[.[] | select(.status == "completed" and .conclusion != "success") | "\(.name) (\(.conclusion))"] | join(", ")' <<<"$pipelines")
  [[ -z $ruim ]] || erro "O pipeline não passou: $ruim. A API deste commit não foi publicada."

  abertos=$(jq '[.[] | select(.status != "completed")] | length' <<<"$pipelines")
  if (( abertos > 0 )); then
    estado="$abertos de $total pipelines da API rodando"
  else
    [[ -n $fim_pipelines ]] || fim_pipelines=$SECONDS
    # só os deploys disparados desde o começo destes pipelines; cada pipeline dispara um
    desde=$(jq -r 'map(.created_at) | min' <<<"$pipelines")
    deploys=$(consulta "repos/$GITHUB_REPOSITORY/actions/workflows/deploy.yml/runs?event=workflow_run&per_page=100" |
      jq --arg desde "$desde" '[.workflow_runs[] | select(.created_at >= $desde)]')
    vistos=$(jq 'length' <<<"$deploys")
    rodando=$(jq '[.[] | select(.status != "completed")] | length' <<<"$deploys")

    ruim=$(jq -r '[.[] | select(.status == "completed" and (.conclusion | IN("success", "skipped", "cancelled") | not)) | .conclusion] | unique | join(", ")' <<<"$deploys")
    [[ -z $ruim ]] || erro "O deploy da API terminou como $ruim."

    if (( rodando == 0 && vistos >= total )); then
      if (( $(jq '[.[] | select(.conclusion == "success")] | length' <<<"$deploys") > 0 )); then
        echo "A API deste commit está no ar: a loja segue."
      elif (( $(jq '[.[] | select(.conclusion == "skipped")] | length' <<<"$deploys") == vistos )); then
        echo "O deploy da API está desligado: a loja segue."
      else
        erro "Nenhum deploy da API terminou com sucesso."
      fi
      exit 0
    fi
    if (( rodando == 0 && SECONDS - fim_pipelines >= GATILHO )); then
      erro "Depois de ${GATILHO}s, só $vistos de $total deploys da API apareceram."
    fi
    estado="deploy da API: $rodando rodando, $vistos de $total disparados"
  fi

  if [[ $estado != "$ultimo" ]]; then
    echo "Esperando: $estado"
    ultimo=$estado
  fi
  sleep "$INTERVALO"
done
