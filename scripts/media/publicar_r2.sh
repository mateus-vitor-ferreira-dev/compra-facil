#!/usr/bin/env bash
# Envia as fotos dos produtos (infra/media/produtos) ao bucket do R2 da produção (#203), em
# produtos/<arquivo>, que é o caminho que o seed monta a partir de CATALOG_SEED_MEDIA_URL.
#
# O nome de cada arquivo leva o hash do conteúdo, então a foto que já está lá não muda: o sync só
# envia as novas (--size-only, sem baixar nada para comparar) e nunca apaga as antigas, que um catálogo
# já semeado ainda pode usar. Cada objeto vai com o cache longo e o tipo certo, que a CDN da
# Cloudflare repassa ao navegador.
#
#   MEDIA_S3_ENDPOINT=https://<account id>.r2.cloudflarestorage.com \
#   MEDIA_S3_ACCESS_KEY_ID=... MEDIA_S3_SECRET_ACCESS_KEY=... scripts/media/publicar_r2.sh
#
#   MEDIA_S3_BUCKET   o bucket (padrão: compra-facil-media)
#   --dry-run         só lista o que seria enviado
#
# Usa o aws-cli da máquina; sem ele, o da imagem amazon/aws-cli.
set -euo pipefail

ROOT=$(cd "$(dirname "$0")/../.." && pwd)
: "${MEDIA_S3_ENDPOINT:?o endpoint S3 do R2 (https://<account id>.r2.cloudflarestorage.com)}"
: "${MEDIA_S3_ACCESS_KEY_ID:?}" "${MEDIA_S3_SECRET_ACCESS_KEY:?}"
BUCKET=${MEDIA_S3_BUCKET:-compra-facil-media}

if [ -z "$(find "$ROOT/infra/media/produtos" -name '*.webp' 2>/dev/null)" ]; then
  echo "infra/media/produtos não tem fotos: rode scripts/media/product_images.py build" >&2
  exit 1
fi

export AWS_ACCESS_KEY_ID=$MEDIA_S3_ACCESS_KEY_ID AWS_SECRET_ACCESS_KEY=$MEDIA_S3_SECRET_ACCESS_KEY
export AWS_DEFAULT_REGION=${AWS_DEFAULT_REGION:-auto}
# o R2 recusa os checksums novos do aws-cli; só quando exigido (como no pg-backup)
export AWS_REQUEST_CHECKSUM_CALCULATION=when_required AWS_RESPONSE_CHECKSUM_VALIDATION=when_required

aws() {
  if command -v aws >/dev/null; then
    command aws "$@"
  else
    docker run --rm -v "$ROOT/infra/media/produtos:$ROOT/infra/media/produtos:ro" \
      -e AWS_ACCESS_KEY_ID -e AWS_SECRET_ACCESS_KEY -e AWS_DEFAULT_REGION \
      -e AWS_REQUEST_CHECKSUM_CALCULATION -e AWS_RESPONSE_CHECKSUM_VALIDATION \
      --network host amazon/aws-cli:2.35.22 "$@"
  fi
}

extra=()
[ "${1:-}" = --dry-run ] && extra=(--dryrun)

aws --endpoint-url "$MEDIA_S3_ENDPOINT" s3 sync "$ROOT/infra/media/produtos" "s3://$BUCKET/produtos" \
  --size-only --no-progress "${extra[@]}" \
  --exclude '*' --include '*.webp' \
  --content-type image/webp \
  --cache-control 'public, max-age=31536000, immutable'

echo "$(find "$ROOT/infra/media/produtos" -name '*.webp' | wc -l) fotos em s3://$BUCKET/produtos"
