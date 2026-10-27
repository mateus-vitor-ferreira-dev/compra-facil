#!/bin/sh
# Um pg_dump (formato custom) de cada banco por dia no bucket, em postgres/<AAAA-MM-DD>/<banco>.dump,
# mais os papéis (pg_dumpall --globals-only). Apaga os dias além da retenção. Depois de cada rodada,
# manda ao Collector a métrica compra_facil.backup.last_success (o horário do último backup inteiro):
# o alerta dispara quando ela envelhece.
#   BACKUP_DATABASES  bancos, separados por vírgula
#   BACKUP_TIME       hora UTC da rodada diária, HH:MM          (padrão: 06:00, 3h em Brasília)
#   BACKUP_RETENTION_DAYS                                       (padrão: 14)
#   BACKUP_NOW=1      roda uma vez e sai (o teste e o backup antes de uma mudança arriscada)
#   S3_BUCKET, S3_ENDPOINT, AWS_ACCESS_KEY_ID, AWS_SECRET_ACCESS_KEY
#   PGHOST, PGUSER, PGPASSWORD                                  (o superusuário)
#   OTLP_ENDPOINT     o Collector, http://otel-collector:4318   (vazio: não manda a métrica)
set -eu

: "${BACKUP_DATABASES:?}" "${S3_BUCKET:?}" "${S3_ENDPOINT:?}" "${PGHOST:?}" "${PGPASSWORD:?}"
BACKUP_TIME=${BACKUP_TIME:-06:00}
RETENTION=${BACKUP_RETENTION_DAYS:-14}
# o R2 e outros S3 compatíveis recusam os checksums novos do aws-cli; só quando exigido
export AWS_REQUEST_CHECKSUM_CALCULATION=when_required AWS_RESPONSE_CHECKSUM_VALIDATION=when_required
export AWS_DEFAULT_REGION="${AWS_DEFAULT_REGION:-auto}"

s3() { aws --endpoint-url "$S3_ENDPOINT" s3 "$@"; }
log() { echo "{\"time\":\"$(date -u +%FT%TZ)\",\"level\":\"$1\",\"msg\":\"$2\"}"; }

metric() { # metric <0|1>: o resultado da rodada
  [ -n "${OTLP_ENDPOINT:-}" ] || return 0
  now=$(date +%s)
  wget -q -O /dev/null --header 'Content-Type: application/json' --post-data "{\"resourceMetrics\":[{
    \"resource\":{\"attributes\":[{\"key\":\"service.name\",\"value\":{\"stringValue\":\"pg-backup\"}}]},
    \"scopeMetrics\":[{\"metrics\":[
      {\"name\":\"compra_facil.backup.last_run_ok\",\"unit\":\"1\",\"gauge\":{\"dataPoints\":[{\"asInt\":\"$1\",\"timeUnixNano\":\"${now}000000000\"}]}}
      $( [ "$1" = 1 ] && echo ",{\"name\":\"compra_facil.backup.last_success\",\"unit\":\"s\",\"gauge\":{\"dataPoints\":[{\"asInt\":\"$now\",\"timeUnixNano\":\"${now}000000000\"}]}}" )
    ]}]}]}" "$OTLP_ENDPOINT/v1/metrics" || log warn "a métrica do backup não chegou ao Collector"
}

# cada passo com "|| return 1": dentro do "if run" do attempt, o set -e não vale, e um upload que falhou
# passaria por sucesso (e a métrica de último backup mentiria)
run() {
  day=$(date -u +%F)
  dir=$(mktemp -d) || return 1
  pg_dumpall --globals-only --no-role-passwords > "$dir/globals.sql" || { rm -rf "$dir"; return 1; }
  for db in $(echo "$BACKUP_DATABASES" | tr ',' ' '); do
    pg_dump --format=custom --dbname="$db" --file="$dir/$db.dump" || { rm -rf "$dir"; return 1; }
  done
  s3 cp --recursive --only-show-errors "$dir" "s3://$S3_BUCKET/postgres/$day/" || { rm -rf "$dir"; return 1; }
  rm -rf "$dir"
  log info "backup de $day no bucket: $BACKUP_DATABASES"

  # retenção: os dias (prefixos AAAA-MM-DD) mais antigos que o limite. Falhar aqui não invalida o backup
  limit=$(date -u -d "@$(( $(date +%s) - RETENTION * 86400 ))" +%Y%m%d)
  s3 ls "s3://$S3_BUCKET/postgres/" | awk '{print $2}' | tr -d / | while read -r old; do
    case $old in [0-9][0-9][0-9][0-9]-[0-9][0-9]-[0-9][0-9]) ;; *) continue ;; esac
    if [ "$(echo "$old" | tr -d -)" -lt "$limit" ]; then
      if s3 rm --recursive --only-show-errors "s3://$S3_BUCKET/postgres/$old/"; then
        log info "backup de $old apagado (retenção de $RETENTION dias)"
      else
        log warn "o backup de $old não foi apagado; fica para a próxima rodada"
      fi
    fi
  done
  return 0
}

attempt() {
  if run; then metric 1; else log error "backup falhou"; metric 0; return 1; fi
}

if [ "${BACKUP_NOW:-}" = 1 ]; then
  attempt
  exit
fi

log info "backup diário às $BACKUP_TIME UTC, $RETENTION dias de retenção"
last=""
while :; do
  today=$(date -u +%F)
  if [ "$(date -u +%H:%M)" = "$BACKUP_TIME" ] && [ "$last" != "$today" ]; then
    last=$today
    # uma falha não derruba o laço: tenta de novo em 10 minutos, até 5 vezes no dia
    n=0
    until attempt; do
      n=$((n + 1))
      [ "$n" -lt 5 ] || break
      sleep 600
    done
  fi
  sleep 30
done
