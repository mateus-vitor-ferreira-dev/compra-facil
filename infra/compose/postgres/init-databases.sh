#!/usr/bin/env bash
# Cria um banco e um usuário dono para cada serviço (database-per-service).
# Roda só na primeira inicialização do volume (docker-entrypoint-initdb.d).
set -euo pipefail

for service in ${SERVICE_DATABASES//,/ }; do
  echo "criando banco e usuário: ${service}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
    CREATE ROLE ${service} LOGIN PASSWORD '${SERVICE_DB_PASSWORD}';
    CREATE DATABASE ${service} OWNER ${service};
    REVOKE ALL ON DATABASE ${service} FROM PUBLIC;
SQL
done
