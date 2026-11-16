#!/usr/bin/env bash
# Confere as credenciais da Efí sem imprimir nenhum segredo: pede o token nas duas APIs
# (Cobranças e Pix com o .p12) e mostra só o status e os escopos concedidos.
# Uso: scripts/efi-conferir.sh homologacao | producao
set -euo pipefail
amb=${1:-homologacao}
[ -f "$HOME/.efi/$amb.env" ] || { echo "uso: $0 homologacao|producao"; exit 2; }
set -a; . "$HOME/.efi/$amb.env"; set +a
id=$EFI_CLIENT_ID; secret=$EFI_CLIENT_SECRET; p12=$EFI_P12
charges=${EFI_CHARGES_URL%/}; pix=${EFI_PIX_URL%/}

[ -n "$id" ] && [ -n "$secret" ] || { echo "faltam Client_Id/Client_Secret no ~/.efi/$amb.env"; exit 1; }
basic=$(printf '%s:%s' "$id" "$secret" | base64 -w0)
body='{"grant_type":"client_credentials"}'
# as respostas (com o token) num arquivo só seu, apagado no fim
resp=$(mktemp); trap 'rm -f "$resp" "${pem:-}"' EXIT

echo "== $amb"
code=$(curl -s -o "$resp" -w '%{http_code}' -X POST "$charges/v1/authorize" \
  -H "Authorization: Basic $basic" -H 'Content-Type: application/json' -d "$body")
echo "Cobranças (cartão): HTTP $code"
[ "$code" = 200 ] || head -c 300 "$resp" | sed 's/"access_token":"[^"]*"/"access_token":"…"/'; echo

if [ ! -s "$p12" ]; then
  echo "Pix: sem o certificado em $p12"
else
  pem=$(mktemp)
  # o .p12 da Efí vem sem senha; o -legacy cobre o RC2 dos certificados antigos
  openssl pkcs12 -in "$p12" -nodes -passin pass: -out "$pem" 2>/dev/null \
    || openssl pkcs12 -legacy -in "$p12" -nodes -passin pass: -out "$pem"
  code=$(curl -s -o "$resp" -w '%{http_code}' --cert "$pem" -X POST "$pix/oauth/token" \
    -H "Authorization: Basic $basic" -H 'Content-Type: application/json' -d "$body")
  echo "Pix: HTTP $code"
  if [ "$code" = 200 ]; then
    echo "escopos: $(jq -r .scope "$resp" | tr ' ' '\n' | sort | tr '\n' ' ')"
    for s in cob.write cob.read payloadlocation.read pix.write pix.read webhook.write webhook.read; do
      jq -r .scope "$resp" | tr ' ' '\n' | grep -qx "$s" || echo "  FALTA o escopo $s"
    done
  else
    head -c 300 "$resp"; echo
  fi
  fi
[ -n "${EFI_PIX_KEY:-}" ] && echo "chave PIX: preenchida" || echo "chave PIX: vazia"
[ ${#PAYMENT_PAYEE_CODE} -eq 32 ] && echo "identificador de conta: ok (32)" || echo "identificador de conta: ${#PAYMENT_PAYEE_CODE} caracteres (esperado 32)"
