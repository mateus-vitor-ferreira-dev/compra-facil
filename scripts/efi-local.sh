#!/usr/bin/env bash
# Sobe o ambiente local com a Efí de verdade no payments (infra/compose/compose.efi.yaml), em vez do payment-sim.
#   scripts/efi-local.sh       (ou task efi:homologacao)
#
# Só a homologação: com a produção, o payments local cadastraria o webhook do PIX da conta de verdade apontando
# para o túnel desta máquina, e os avisos da loja no ar passariam a vir para cá.
#
# Lê ~/.efi/homologacao.env (fora do repositório, chmod 600), com os nomes do Coolify: EFI_CLIENT_ID,
# EFI_CLIENT_SECRET, EFI_PIX_KEY, EFI_PIX_WEBHOOK_SECRET, EFI_CHARGES_URL, EFI_PIX_URL, PAYMENT_PAYEE_CODE e EFI_P12
# (o caminho do .p12). Nenhum valor é impresso.
set -euo pipefail
cd "$(dirname "$0")/.."

amb=homologacao
envfile="$HOME/.efi/$amb.env"
[ -f "$envfile" ] || { echo "falta o $envfile"; exit 1; }
set -a; . "$envfile"; set +a
for v in EFI_CLIENT_ID EFI_CLIENT_SECRET EFI_PIX_KEY EFI_PIX_WEBHOOK_SECRET EFI_CHARGES_URL EFI_PIX_URL EFI_P12; do
  [ -n "${!v:-}" ] || { echo "falta o $v no $envfile"; exit 1; }
done
[ -s "$EFI_P12" ] || { echo "falta o certificado em $EFI_P12"; exit 1; }
EFI_PIX_CERTIFICATE_BASE64=$(base64 -w0 "$EFI_P12")
export EFI_PIX_CERTIFICATE_BASE64

compose=(docker compose -f compose.yaml -f infra/compose/compose.efi.yaml)

# 1. o túnel primeiro, com um endereço provisório (o compose exige o EFI_PUBLIC_URL para ler o arquivo)
export EFI_PUBLIC_URL=https://aguardando.invalid
"${compose[@]}" up -d --no-deps efi-tunnel
url=""
for _ in $(seq 1 30); do
  url=$("${compose[@]}" logs efi-tunnel 2>&1 | grep -oE 'https://[a-z0-9-]+\.trycloudflare\.com' | tail -1 || true)
  [ -n "$url" ] && break
  sleep 1
done
[ -n "$url" ] || { echo "o túnel não deu o endereço em 30 s: docker compose logs efi-tunnel"; exit 1; }
# o endereço sai antes da conexão: sem ela (a rede bloqueia a porta 7844, a do túnel), os avisos não chegam
for _ in $(seq 1 30); do
  "${compose[@]}" logs efi-tunnel 2>&1 | grep -q 'Registered tunnel connection' && break
  sleep 1
done
"${compose[@]}" logs efi-tunnel 2>&1 | grep -q 'Registered tunnel connection' || {
  echo "o túnel não conectou: a rede deve bloquear a porta 7844 (TCP e UDP), a do Cloudflare Tunnel"
  "${compose[@]}" rm -sf efi-tunnel >/dev/null
  exit 1
}
export EFI_PUBLIC_URL=$url

# 2. o resto, com o endereço do túnel nos avisos da Efí (o payments cadastra o webhook do PIX ao subir)
"${compose[@]}" up -d --build --wait
echo
echo "Efí (homologação) no payments; avisos em $EFI_PUBLIC_URL/payments/v1/webhooks/efi/{pix,charges}"
echo "O webhook do PIX: ${compose[*]} logs payments | grep -i webhook"
echo
echo "O front, em outro terminal:"
# só o identificador de conta vai para o front: as credenciais não entram no processo dele
echo "  cd frontend/web && PAYMENT_PROVIDER=sandbox \\"
echo "    PAYMENT_PAYEE_CODE=\$(sed -n 's/^PAYMENT_PAYEE_CODE=//p' $envfile) npm run dev"
echo
echo "Para voltar ao simulador: task up"
