# syntax=docker/dockerfile:1
# Imagem dos serviços .NET (orders, inventory, payments, notifications, identity): um Dockerfile para os
# cinco, o serviço vem por build arg. Runtime chiseled: sem shell, sem gerenciador de pacotes,
# usuário não-root. Multi-arquitetura (amd64 e arm64, a VM da Oracle é ARM) por compilação cruzada:
# o SDK roda na arquitetura de quem builda e publica para a do alvo; o runtime só copia, sem emular.
# Contexto do build: raiz do repositório (usa backend/Directory.*.props, backend/libs/dotnet e contracts/).
#   docker build -f backend/services/dotnet.Dockerfile --build-arg SERVICE=orders \
#     --build-arg PROJECT=CompraFacil.Orders.Api --build-arg PORT=8002 .

FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
ARG TARGETARCH
ARG SERVICE
ARG PROJECT
WORKDIR /src

# mesma disposição do repositório: o Directory.Build.props acha os contratos em ../contracts
COPY .editorconfig ./
COPY backend/global.json backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY contracts contracts
COPY backend/libs/dotnet backend/libs/dotnet
COPY backend/services/${SERVICE}/src backend/services/${SERVICE}/src

# não pode se chamar VERSION: ARG vira variável de ambiente no RUN, o MSBuild a lê como $(Version)
# e o restore falha com "dev" (versão inválida)
ARG APP_VERSION=dev
# sharing=locked: os 5 serviços buildam em paralelo e extrairiam o mesmo pacote ao mesmo tempo
# TARGETARCH do Docker (amd64, arm64) no nome do .NET (x64, arm64)
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    arch=$([ "$TARGETARCH" = "arm64" ] && echo arm64 || echo x64) && \
    dotnet publish backend/services/${SERVICE}/src/${PROJECT}/${PROJECT}.csproj \
      -c Release -a "$arch" --self-contained false -o /app -p:InformationalVersion=${APP_VERSION} && \
    # nome fixo para o ENTRYPOINT/HEALTHCHECK, que não expandem build args
    ln -s ${PROJECT} /app/service

FROM mcr.microsoft.com/dotnet/aspnet:10.0.12-noble-chiseled AS runtime
ARG PORT
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_HTTP_PORTS=${PORT}
EXPOSE ${PORT}
HEALTHCHECK --interval=10s --timeout=3s --start-period=15s --retries=3 CMD ["/app/service", "healthcheck"]
ENTRYPOINT ["/app/service"]
