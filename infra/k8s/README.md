# Kubernetes local (k3d + Helm)

O mesmo sistema do compose num cluster Kubernetes de verdade (k3s, via k3d), para mostrar a escala
independente funcionando sozinha: o **HPA** sobe réplicas do catalog quando a CPU passa da meta e as
derruba depois do pico, sem tocar nos outros serviços (#93).

Precisa de Docker, [k3d](https://k3d.io) 5.9+, [kubectl](https://kubernetes.io/docs/tasks/tools/) e
[Helm](https://helm.sh) no PATH.

```bash
task k8s:up             # cluster, imagens, dependências e os 6 serviços (≈ 3 min na primeira vez)
task k8s:seed           # 10 mil produtos, categorias e campanhas
task k8s:load -- 600 2m # k6 a 600 req/s no pico por 2 min, dentro do cluster; mostra o que o HPA fez
task k8s:down           # apaga o cluster
```

A API fica em **http://localhost:8088** (`/catalog`, `/orders`, `/inventory`, `/payments`, `/identity`), pelo Traefik
que vem com o k3s.

## O que tem aqui

| Arquivo | O que é |
|---|---|
| `helm/compra-facil-service/` | Chart genérico: Deployment (sondas `/health` e `/ready`), Service (HTTP e gRPC), HPA por CPU e rota no Traefik (Ingress + Middleware que tira o prefixo) |
| `helm/values/<serviço>.yaml` | Imagem, porta, variáveis e recursos de cada serviço. Só o catalog liga o HPA (1 a 5 réplicas, meta de 60% de CPU) |
| `deps.yaml` | Postgres (mesmo script de bancos do compose), Redis, Redpanda e Mailpit, uma instância de cada |
| `seed-job.yaml`, `k6-job.yaml` | Jobs do seed e da carga; o k6 usa o mesmo `load-tests/k6/campanha.js` do compose |

## Resultado

Com `task k8s:load -- 600 2m` (90% leitura, 10% compras), o HPA levou o catalog de **1 para 2 e depois
para 4 réplicas** durante o pico e de volta a **1** um minuto depois:

```
SuccessfulRescale   New size: 2; reason: cpu resource utilization (percentage of request) above target
SuccessfulRescale   New size: 4; reason: cpu resource utilization (percentage of request) above target
SuccessfulRescale   New size: 3; reason: All metrics below target
SuccessfulRescale   New size: 1; reason: All metrics below target
```

O k6 fez 88 mil requisições com **0% de erro** e **p95 de 5,4 ms**. Orders, inventory, payments e
notifications ficaram com uma réplica o tempo todo.

## Decisões e limites

- **O k6 passa pelo Traefik**, não pelo Service do catalog: o Traefik balanceia por requisição entre as
  réplicas novas, e o Service (kube-proxy) balanceia por conexão, então as conexões já abertas do k6
  ficariam presas no primeiro pod.
- **O gRPC do orders para o catalog é uma conexão HTTP/2 longa**, presa a um pod. Para o pico de leitura
  da vitrine isso não importa (o gargalo é o HTTP), mas balancear o gRPC exigiria um balanceador L7, como
  um service mesh ou o balanceamento no cliente gRPC.
- **Sem OpenTelemetry no cluster:** os serviços sobem sem `OTEL_EXPORTER_OTLP_ENDPOINT`, então não exportam
  nada. A observabilidade completa continua no compose.
- **Segredos de desenvolvimento** (senhas e o JWT de dev) ficam nos values, porque o cluster é local. Em
  produção viriam de Secrets, e as dependências de serviços gerenciados ou operators.
