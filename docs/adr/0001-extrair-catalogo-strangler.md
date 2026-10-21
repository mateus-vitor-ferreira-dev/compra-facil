# ADR 0001 — Extrair o Catálogo com Strangler Fig

- **Status:** aceito
- **Data:** 2026-10-21

## Contexto

O catálogo concentra o pico de carga das campanhas, e o time de catálogo depende da implantação
conjunta. A equipe tem pouca experiência com sistemas distribuídos (ver `docs/Trabalho_MonolitoXMicrosservicos.pdf`).

## Decisão

1. Primeiro, organizar o monolito em módulos com fronteiras testadas (Proposta A como pré-requisito da B).
2. Extrair só o Catálogo, para um serviço em Go com banco próprio.
3. Colocar o Traefik na frente, rotear uma fatia crescente do tráfego e espelhar requisições para comparar.
4. Os dados do catálogo passam a pertencer ao serviço. O monolito mantém uma cópia somente leitura,
   alimentada por eventos Kafka.
5. Desligar o módulo legado só depois que os critérios de `docs/stack.md` forem atendidos.

## Consequências

- (+) Escala e deploy independentes para o catálogo.
- (+) Cada passo é reversível, porque o peso no gateway volta para 0%.
- (−) Consistência eventual entre o catálogo e o read model do monolito.
- (−) Exige observabilidade, contratos e testes de integração antes do corte.
