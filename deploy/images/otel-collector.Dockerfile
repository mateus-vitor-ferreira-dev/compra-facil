# O Collector da produção, com a configuração dentro da imagem: o Coolify não monta arquivos do repositório.
FROM otel/opentelemetry-collector-contrib:0.162.0
COPY deploy/coolify/otel-collector.yaml /etc/otelcol/config.yaml
