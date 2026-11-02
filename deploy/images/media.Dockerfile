# As fotos dos produtos, dentro da imagem, servidas pelo gateway em /media.
FROM caddy:2.11.6-alpine
COPY infra/media/Caddyfile /etc/caddy/Caddyfile
COPY infra/media/produtos /srv/produtos
