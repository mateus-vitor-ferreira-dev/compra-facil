# syntax=docker/dockerfile:1
# Imagem do catalog: build estático em Go, runtime distroless sem shell, usuário não-root.
# Multi-arquitetura (amd64 e arm64) por compilação cruzada: o Go roda na arquitetura de quem builda.
# Contexto do build: services/catalog (docker build -t catalog services/catalog)
# O seed tem imagem própria (--target seed) para não pesar na imagem da api.

FROM --platform=$BUILDPLATFORM golang:1.26.10-alpine3.24 AS build
ARG TARGETOS=linux
ARG TARGETARCH
WORKDIR /src

# dependências primeiro: a camada só é refeita quando go.mod/go.sum mudam
COPY go.mod go.sum ./
RUN --mount=type=cache,target=/go/pkg/mod go mod download

COPY . .
ARG VERSION=dev
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    export CGO_ENABLED=0 GOOS=$TARGETOS GOARCH=$TARGETARCH && \
    go build -trimpath -ldflags="-s -w -X main.version=${VERSION}" -o /out/api ./cmd/api && \
    go build -trimpath -ldflags="-s -w" -o /out/seed ./cmd/seed

FROM gcr.io/distroless/static-debian13:nonroot AS runtime
WORKDIR /app
COPY config/config.yaml /app/config/config.yaml
USER nonroot:nonroot

FROM runtime AS seed
COPY --from=build /out/seed /app/seed
ENTRYPOINT ["/app/seed"]

# última etapa = alvo padrão do build
FROM runtime AS api
COPY --from=build /out/api /app/api
EXPOSE 8001 9001
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 CMD ["/app/api", "healthcheck"]
ENTRYPOINT ["/app/api"]
