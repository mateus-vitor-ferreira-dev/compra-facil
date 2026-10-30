# O gateway da loja de demonstração, com a configuração dentro da imagem: o Coolify não monta arquivos do repositório.
FROM traefik:v3.7.16
COPY deploy/coolify/traefik.yml /etc/traefik/traefik.yml
COPY infra/gateway/traefik/dynamic /etc/traefik/dynamic
