#!/usr/bin/env bash
# Publica a API no Coolify: fixa a versão de cada imagem, pede o deploy, espera o desfecho e, quando
# falha, diz de quem é a falha. É o que o workflow deploy.yml roda depois das imagens da main.
#
# - A versão é fixa por deploy: cada imagem ganha a tag deploy-<id> (a :main daquele instante), e o
#   IMAGE_TAG da stack passa a ser ela. O que está no ar é sempre uma tag que não muda, e voltar é
#   trocar o IMAGE_TAG para a do deploy anterior (deploy/README.md).
# - Espera o desfecho, e não só o aceite: o pedido de deploy volta assim que entra na fila.
# - Separa "o Coolify não atendeu" (reexecutar o job) de "o deploy quebrou" (corrigir o código).
#
# Variáveis:
#   COOLIFY_URL, COOLIFY_TOKEN, COOLIFY_APP_UUID   o painel, o token da API e o recurso da API
#   DEPLOY_TAG        a tag desta publicação           (ex.: deploy-123456)
#   IMAGES            as imagens, separadas por espaço  (ghcr.io/<dono>/compra-facil-catalog ...)
#   DEPLOY_TIMEOUT    segundos até desistir             (padrão: 900)
set -euo pipefail

: "${COOLIFY_URL:?}" "${COOLIFY_TOKEN:?}" "${COOLIFY_APP_UUID:?}" "${DEPLOY_TAG:?}" "${IMAGES:?}"
TIMEOUT=${DEPLOY_TIMEOUT:-900}
SUMMARY=${GITHUB_STEP_SUMMARY:-/dev/null}
API="${COOLIFY_URL%/}/api/v1"

call() { curl -sS --max-time 30 -H "Authorization: Bearer $COOLIFY_TOKEN" -H 'Content-Type: application/json' "$@"; }

platform_down() {
  echo "::error title=Coolify não atendeu::$1 O código não está em causa."
  cat >> "$SUMMARY" <<MD
### 🟡 O Coolify não aceitou o deploy

$1 **Não é defeito do código**: nada mudou em produção.

Confira se o painel (<${COOLIFY_URL}>) e o VPS estão no ar e se o secret \`COOLIFY_TOKEN\` vale; depois **reexecute o job**.
MD
  exit 1
}

# ── 1. Fixa a versão: a :main de agora de cada imagem ganha a tag deste deploy ─────────────────────
for image in $IMAGES; do
  docker buildx imagetools create --tag "$image:$DEPLOY_TAG" "$image:main" >/dev/null
  echo "$image:$DEPLOY_TAG = $(docker buildx imagetools inspect "$image:$DEPLOY_TAG" --format '{{json .Manifest.Digest}}')"
done

# ── 2. A stack passa a usar essa tag ─────────────────────────────────────────────────────────────
code=$(call -o /dev/null -w '%{http_code}' -X PATCH "$API/applications/$COOLIFY_APP_UUID/envs" \
  -d "{\"key\":\"IMAGE_TAG\",\"value\":\"$DEPLOY_TAG\"}") || platform_down "O IMAGE_TAG não chegou ao Coolify."
[[ $code == 2* ]] || platform_down "O Coolify recusou o IMAGE_TAG com HTTP $code."

# ── 3. Pede o deploy ──────────────────────────────────────────────────────────────────────────────
status=0
response=$(call -X POST "$API/deploy?uuid=$COOLIFY_APP_UUID" -w '\n%{http_code}') || status=$?
[[ $status -eq 0 ]] || platform_down "O pedido de deploy não chegou ao Coolify (curl saiu com $status)."
http=$(tail -n1 <<<"$response")
body=$(sed '$d' <<<"$response")
[[ $http == 200 ]] || platform_down "O Coolify recusou o pedido de deploy com HTTP $http."
deployment=$(jq -r '.deployments[0].deployment_uuid // empty' <<<"$body")
[[ -n $deployment ]] || platform_down "O Coolify respondeu sem id de deploy: $(jq -c '.deployments[0].message // .' <<<"$body")"
echo "Deploy $deployment ($DEPLOY_TAG) na fila. Aguardando o desfecho (limite: ${TIMEOUT}s)."

# ── 4. Espera o desfecho ──────────────────────────────────────────────────────────────────────────
# finished é o único "está no ar"; failed é variável faltando, imagem que não baixou ou contêiner que
# não ficou saudável; cancelled-by-user é alguém parando pelo painel. Verde aqui afirma que a produção
# está com esta versão, então os dois últimos ficam vermelhos.
start=$SECONDS
last=""
while :; do
  json=$(call "$API/deployments/$deployment" 2>/dev/null || echo '{}')
  current=$(jq -r '.status // empty' <<<"$json" 2>/dev/null || true)
  if [[ -n $current && $current != "$last" ]]; then
    echo "Deploy $deployment: $current"
    last=$current
  fi
  case $current in
    finished)
      echo "No ar: $DEPLOY_TAG"
      echo "### 🟢 No ar: \`$DEPLOY_TAG\`" >> "$SUMMARY"
      exit 0
      ;;
    failed | cancelled-by-user)
      echo "::error title=O deploy quebrou::O Coolify aceitou o deploy e ele terminou como $current."
      # só as linhas não ocultas: nas ocultas (comandos internos do Coolify) podem aparecer variáveis
      echo "Últimas linhas do log do deploy:"
      jq -r '.logs // "[]" | fromjson | map(select(.hidden == false) | .output) | .[-80:] | .[]' <<<"$json" 2>/dev/null ||
        echo "(o log não veio; veja o deploy no painel)"
      cat >> "$SUMMARY" <<MD
### 🔴 O deploy falhou no Coolify

O deploy \`$deployment\` (\`$DEPLOY_TAG\`) terminou como \`$current\`. O log está no passo acima. **A causa é deste commit ou da configuração da stack** (uma variável faltando, uma migration, um contêiner que não ficou saudável).
MD
      exit 1
      ;;
  esac
  if (( SECONDS - start >= TIMEOUT )); then
    echo "::error title=O deploy não terminou::$deployment não terminou em ${TIMEOUT}s (último status: ${last:-desconhecido})."
    echo "### 🟡 O deploy não terminou em ${TIMEOUT}s: confira \`$deployment\` no painel antes de reexecutar." >> "$SUMMARY"
    exit 1
  fi
  sleep 10
done
