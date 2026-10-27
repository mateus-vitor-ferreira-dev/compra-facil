#!/usr/bin/env bash
# Cria um banco e um usuário dono para cada serviço (database-per-service).
# Idempotente: roda na primeira inicialização do volume (docker-entrypoint-initdb.d) e, no compose, a
# cada subida pelo postgres-databases, para um serviço novo ganhar o banco num volume que já existia.
# Fora do entrypoint, conecta pelas variáveis PGHOST/PGPASSWORD.
# A senha de cada um vem de SERVICE_DB_PASSWORD_<SERVIÇO> (a produção tem uma por serviço) ou, sem ela,
# de SERVICE_DB_PASSWORD (a mesma para todos, no ambiente local). A senha de quem já existe é acertada a
# cada subida: trocar a variável troca a senha no banco.
set -euo pipefail

for service in ${SERVICE_DATABASES//,/ }; do
  password_var="SERVICE_DB_PASSWORD_${service^^}"
  password=${!password_var:-${SERVICE_DB_PASSWORD:?defina SERVICE_DB_PASSWORD ou $password_var}}
  echo "banco e usuário: ${service}"
  psql -v ON_ERROR_STOP=1 -v service="$service" -v password="$password" \
    --username "$POSTGRES_USER" --dbname postgres <<-'SQL'
    SELECT format('CREATE ROLE %I LOGIN', :'service')
    WHERE NOT EXISTS (SELECT FROM pg_roles WHERE rolname = :'service')\gexec
    SELECT format('ALTER ROLE %I PASSWORD %L', :'service', :'password')\gexec
    SELECT format('CREATE DATABASE %I OWNER %I', :'service', :'service')
    WHERE NOT EXISTS (SELECT FROM pg_database WHERE datname = :'service')\gexec
    SELECT format('REVOKE ALL ON DATABASE %I FROM PUBLIC', :'service')\gexec
SQL
done
