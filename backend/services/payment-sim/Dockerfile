# syntax=docker/dockerfile:1
# Imagem do provedor de pagamento simulado: build estático em Go, runtime distroless sem shell, usuário não-root.
# Multi-arquitetura (amd64 e arm64) por compilação cruzada: o Go roda na arquitetura de quem builda.
# Contexto do build: services/payment-sim (docker build -t payment-sim services/payment-sim)
# Dublê de teste: sobe em dev/CI, não vai para a VPS.

FROM --platform=$BUILDPLATFORM golang:1.26.10-alpine3.24 AS build
ARG TARGETOS=linux
ARG TARGETARCH
WORKDIR /src

# dependências primeiro: a camada só é refeita quando go.mod/go.sum mudam
COPY go.mod go.sum ./
RUN --mount=type=cache,target=/go/pkg/mod go mod download

COPY . .
RUN --mount=type=cache,target=/go/pkg/mod \
    --mount=type=cache,target=/root/.cache/go-build \
    export CGO_ENABLED=0 GOOS=$TARGETOS GOARCH=$TARGETARCH && \
    go build -trimpath -ldflags="-s -w" -o /out/sim ./cmd/sim

FROM gcr.io/distroless/static-debian13:nonroot
WORKDIR /app
COPY --from=build /out/sim /app/sim
USER nonroot:nonroot
EXPOSE 8090
HEALTHCHECK --interval=10s --timeout=3s --start-period=5s --retries=3 CMD ["/app/sim", "healthcheck"]
ENTRYPOINT ["/app/sim"]
