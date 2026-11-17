#!/bin/sh
# Restaura bancos de um dia do bucket. Por padrão no próprio banco (apaga e recria os objetos); com
# RESTORE_SUFFIX, num banco ao lado (<banco><sufixo>), para comparar ou copiar só o que faltou.
#   restore.sh <AAAA-MM-DD> <banco> [banco...]  < chave-privada-do-age
# Mesmas variáveis do backup.sh (S3_*, AWS_*, PG*). Os dumps estão cifrados com o age: a chave privada (o
# arquivo do age-keygen, que fica com o dono e não no servidor) entra pela entrada padrão, ou por
# BACKUP_AGE_IDENTITY_FILE. Ver deploy/README.md.
set -eu
umask 077
day=${1:?uso: restore.sh <AAAA-MM-DD> <banco> [banco...]}
shift
[ $# -gt 0 ] || { echo "diga quais bancos" >&2; exit 1; }
export AWS_REQUEST_CHECKSUM_CALCULATION=when_required AWS_RESPONSE_CHECKSUM_VALIDATION=when_required
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-auto}"
dir=$(mktemp -d)
trap 'rm -rf "$dir"' EXIT
key=${BACKUP_AGE_IDENTITY_FILE:-}
if [ -z "$key" ]; then
  [ ! -t 0 ] || { echo "mande a chave privada do age pela entrada padrão (restore.sh ... < chave)" >&2; exit 1; }
  key="$dir/chave"
  cat > "$key"
fi
grep -q '^AGE-SECRET-KEY-' "$key" || { echo "isso não é uma chave privada do age" >&2; exit 1; }
for db in "$@"; do
  aws --endpoint-url "$S3_ENDPOINT" s3 cp --only-show-errors "s3://$S3_BUCKET/postgres/$day/$db.dump.age" "$dir/$db.dump.age"
  age -d -i "$key" -o "$dir/$db.dump" "$dir/$db.dump.age"
  rm -f "$dir/$db.dump.age"
  target="$db${RESTORE_SUFFIX:-}"
  if [ -n "${RESTORE_SUFFIX:-}" ]; then
    psql -v ON_ERROR_STOP=1 -d postgres -c "DROP DATABASE IF EXISTS \"$target\"" -c "CREATE DATABASE \"$target\" OWNER \"$db\""
  fi
  # o dono dos objetos é o usuário do serviço, como no original
  pg_restore --clean --if-exists --no-owner --role="$db" --dbname="$target" --single-transaction "$dir/$db.dump"
  echo "restaurado: $db de $day em $target"
done
