#!/usr/bin/env bash
# aviso-manutencao.sh on|off: liga ou desliga a marca do deploy no Cloudflare KV, que a loja lê para trocar o erro
# por "estamos atualizando a loja" e segurar os pedidos enquanto a API é publicada (#370). Chamado pelo deploy.yml.
#
# A marca liga com prazo (MANUTENCAO_TTL, padrão 900 s): se o job morrer no meio, ela some sozinha. O KV leva
# alguns segundos para valer em todo lugar, e a loja guarda a leitura por até 30 s.
#
# Nunca reprova o deploy: sem as variáveis, ou com o Cloudflare fora, avisa e segue (a loja mostra o erro de antes).
#
# Variáveis:
#   CLOUDFLARE_API_TOKEN          com a permissão Workers KV Storage: Edit
#   CLOUDFLARE_ACCOUNT_ID         a conta
#   CLOUDFLARE_KV_MAINTENANCE_ID  o namespace do binding MAINTENANCE (wrangler.jsonc)
set -uo pipefail

acao=${1:?on ou off}
if [[ -z ${CLOUDFLARE_API_TOKEN:-} || -z ${CLOUDFLARE_ACCOUNT_ID:-} || -z ${CLOUDFLARE_KV_MAINTENANCE_ID:-} ]]; then
  echo "::warning title=Aviso de manutenção desligado::Sem CLOUDFLARE_API_TOKEN, CLOUDFLARE_ACCOUNT_ID ou CLOUDFLARE_KV_MAINTENANCE_ID: a loja não avisa o deploy."
  exit 0
fi

url="https://api.cloudflare.com/client/v4/accounts/$CLOUDFLARE_ACCOUNT_ID/storage/kv/namespaces/$CLOUDFLARE_KV_MAINTENANCE_ID/values/deploy"
auth=(-H "Authorization: Bearer $CLOUDFLARE_API_TOKEN")
case $acao in
  on)
    code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 -X PUT "${auth[@]}" -H 'Content-Type: text/plain' \
      "$url?expiration_ttl=${MANUTENCAO_TTL:-900}" --data "{\"since\":\"$(date -u +%Y-%m-%dT%H:%M:%SZ)\"}" || echo 000)
    ;;
  off)
    code=$(curl -sS -o /dev/null -w '%{http_code}' --max-time 15 -X DELETE "${auth[@]}" "$url" || echo 000)
    ;;
  *)
    echo "uso: $0 on|off" >&2
    exit 2
    ;;
esac

if [[ $code == 2* ]]; then
  echo "Aviso de manutenção: $acao."
else
  echo "::warning title=Aviso de manutenção::O Cloudflare respondeu $code ao $acao. O deploy segue; a loja pode não avisar."
fi
