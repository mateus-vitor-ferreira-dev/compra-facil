#!/usr/bin/env bash
# Cria um banco e um usuário dono para cada serviço (database-per-service).
# Idempotente: roda na primeira inicialização do volume (docker-entrypoint-initdb.d) e, no compose, a
# cada subida pelo postgres-databases, para um serviço novo ganhar o banco num volume que já existia.
# Fora do entrypoint, conecta pelas variáveis PGHOST/PGPASSWORD.
set -euo pipefail

for service in ${SERVICE_DATABASES//,/ }; do
  echo "banco e usuário: ${service}"
  psql -v ON_ERROR_STOP=1 --username "$POSTGRES_USER" --dbname postgres <<-SQL
    SELECT format('CREATE ROLE %I LOGIN PASSWORD %L', '${service}', '${SERVICE_DB_PASSWORD}')
    WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = '${service}')\gexec
    SELECT format('CREATE DATABASE %I OWNER %I', '${service}', '${service}')
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = '${service}')\gexec
    REVOKE ALL ON DATABASE ${service} FROM PUBLIC;
SQL
done
