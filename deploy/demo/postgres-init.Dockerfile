# Cria os bancos e os usuários dos serviços e acerta as senhas, a cada deploy (idempotente).
FROM postgres:17.11-alpine
COPY infra/compose/postgres/init-databases.sh /init-databases.sh
