# ADR 0001 — Microsserviços divididos por time, com stack poliglota

- **Status:** aceito
- **Data:** 2026-10-21

## Contexto

O catálogo concentra o pico de carga das campanhas. Os dois times (catálogo/campanhas e compras) hoje
dependem de uma implantação conjunta (ver `docs/_Trabalho_MonolitoXMicrosservicos.pdf`).

## Decisão

1. Cinco serviços: `catalog`, `orders`, `inventory`, `payments` e `notifications`. Cada um tem banco próprio.
2. O time de catálogo usa **Go + Gin + GORM**. O time de compras usa **ASP.NET Core + EF Core**.
3. Comunicação síncrona só onde é preciso (Orders → Catalog via gRPC). O resto usa eventos Kafka com outbox.
4. O fluxo de compra é uma SAGA coreografada, com compensações.
5. Todos os contratos ficam em `contracts/` e são validados no CI.

## Consequências

- (+) Escala e deploy independentes, em especial do catálogo.
- (+) Cada time escolhe a stack que atende ao seu perfil de carga.
- (−) Consistência eventual e mais peças para operar.
- (−) Observabilidade, contratos e testes de integração passam a ser obrigatórios.
