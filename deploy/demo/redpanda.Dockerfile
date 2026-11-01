# O Redpanda com a configuração de produção dentro da imagem.
FROM redpandadata/redpanda:v26.2.5
COPY deploy/coolify/redpanda.yaml /config/redpanda.yaml
COPY deploy/coolify/redpanda.bootstrap.yaml /config/redpanda.bootstrap.yaml
