# Resultados de entrega: autonomia por serviço

Critério observável 2 do parecer (#91). Gerado por `python3 scripts/delivery_metrics.py` em 2026-10-22 18:01 UTC, a partir
do histórico do GitHub Actions.

## Como foi medido

- **Deploy:** imagem do serviço publicada no GHCR (job "imagem no GHCR") por um push em `develop` ou `main`, que
  é o merge de um PR. Cada serviço tem o próprio pipeline, disparado só pelos caminhos dele
  (`.github/workflows/<serviço>.yml`).
- **Lead time:** do primeiro commit do PR até a imagem publicada.
- **Deploy sozinho:** o merge publicou só esse serviço. É a medida direta de "um time entrega sem
  coordenar com o outro". Mudanças na biblioteca comum .NET (`libs/dotnet`) publicam os quatro serviços
  .NET juntos, e isso aparece aqui.
- **Período:** de 2026-10-22 16:07 a 2026-10-22 17:57 UTC, com 21 deploys. O
  histórico é curto (o CI por serviço existe desde o PR #122): com menos de uma semana de dados, "por
  semana" é o total do período.

## Por serviço

| Serviço | Deploys | Por semana | Sozinho (sem outros serviços) | Lead time (mediana) | Lead time (máx.) |
|---|---|---|---|---|---|
| catalog | 5 | 5.0 | 1 de 5 | 25 min | 39 min |
| orders | 4 | 4.0 | 0 de 4 | 21 min | 32 min |
| inventory | 4 | 4.0 | 0 de 4 | 21 min | 33 min |
| payments | 4 | 4.0 | 0 de 4 | 21 min | 32 min |
| notifications | 4 | 4.0 | 0 de 4 | 21 min | 32 min |

## Por merge

| Merge | Serviços publicados |
|---|---|
| #128 feat(orders): resiliência na chamada ao catalog (Polly) com fallback para a cópia local | catalog, inventory, notifications, orders, payments |
| #127 feat: tópicos de retry e dead letter topic com reprocessamento | catalog, inventory, notifications, orders, payments |
| #124 feat: observabilidade com OpenTelemetry e instrumentação dos serviços | catalog, inventory, notifications, orders, payments |
| #122 ci: pipelines por serviço (path filters + GHCR) e contratos | catalog, inventory, notifications, orders, payments |
| #123 docs(catalog): TTL do cache e o início da próxima campanha | catalog |

4 de 5 merges publicaram mais de um serviço.

## Por que um merge publica vários serviços

O pipeline de cada serviço dispara pelos caminhos que o afetam. Três tipos de mudança publicam mais de um:

- **Código comum dos serviços .NET** (`libs/dotnet`, `Directory.Build.props`, `Directory.Packages.props`,
  `services/dotnet.Dockerfile`): os quatro serviços de compras são republicados juntos. É o preço da biblioteca
  compartilhada (outbox, Kafka, OpenTelemetry). Uma versão publicada como pacote, que cada serviço atualiza
  quando quiser, trocaria esse acoplamento de deploy por um de versão.
- **Contratos:** quem publica ou consome um contrato é republicado quando ele muda. Até o #91, o pipeline do
  catalog disparava com qualquer arquivo em `contracts/`; agora ele só olha os contratos que serve.
- **Trabalho de plataforma** (CI, observabilidade): atravessa todos os serviços de propósito.

Mudança só dentro de um serviço publica só ele. O #123, por exemplo, publicou apenas o catalog.
