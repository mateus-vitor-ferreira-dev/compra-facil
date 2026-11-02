# As fotos dos produtos, dentro da imagem, servidas pelo gateway em /media.
FROM caddy:2.11.6-alpine
COPY infra/media/Caddyfile /etc/caddy/Caddyfile
COPY infra/media/produtos /srv/produtos
# sem healthcheck, o Coolify mostra a stack inteira como running:unknown (#276)
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 CMD ["wget", "-q", "--spider", "http://127.0.0.1:8080/health"]
