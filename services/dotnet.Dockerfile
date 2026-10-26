# syntax=docker/dockerfile:1
# Imagem dos serviços .NET (orders, inventory, payments, notifications, identity): um Dockerfile para os
# cinco, o serviço vem por build arg. Runtime chiseled: sem shell, sem gerenciador de pacotes,
# usuário não-root.
# Contexto do build: raiz do repositório (usa Directory.*.props, libs/dotnet e contracts/openapi).
#   docker build -f services/dotnet.Dockerfile --build-arg SERVICE=orders \
#     --build-arg PROJECT=CompraFacil.Orders.Api --build-arg PORT=8002 .

FROM mcr.microsoft.com/dotnet/sdk:10.0.401-noble AS build
ARG SERVICE
ARG PROJECT
WORKDIR /src

COPY global.json Directory.Build.props Directory.Packages.props .editorconfig ./
COPY contracts contracts
COPY libs/dotnet libs/dotnet
COPY services/${SERVICE}/src services/${SERVICE}/src

# não pode se chamar VERSION: ARG vira variável de ambiente no RUN, o MSBuild a lê como $(Version)
# e o restore falha com "dev" (versão inválida)
ARG APP_VERSION=dev
# sharing=locked: os 5 serviços buildam em paralelo e extrairiam o mesmo pacote ao mesmo tempo
RUN --mount=type=cache,target=/root/.nuget/packages,sharing=locked \
    dotnet publish services/${SERVICE}/src/${PROJECT}/${PROJECT}.csproj \
      -c Release -o /app -p:InformationalVersion=${APP_VERSION} && \
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
