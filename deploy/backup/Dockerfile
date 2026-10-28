# syntax=docker/dockerfile:1
# O backup do Postgres da produção: um pg_dump de cada banco por dia para um bucket S3 compatível (o R2),
# com retenção, e o restore. O cliente do Postgres é o da mesma versão do servidor.
FROM postgres:17.11-alpine
RUN apk add --no-cache aws-cli
COPY --chmod=0755 backup.sh restore.sh /usr/local/bin/
USER postgres
WORKDIR /tmp
HEALTHCHECK --interval=1m --timeout=5s CMD ["pgrep", "-f", "backup.sh"]
ENTRYPOINT ["backup.sh"]
