# Produção (k3s na Oracle + Argo CD)

O cluster de produção é o k3s da VM criada por `infra/oci` (#157). Tudo o que roda nele vem daqui, por
GitOps: o Argo CD lê esta pasta na branch `main` e aplica sozinho. Um merge na `main` é um deploy.

| Pasta | O que é |
|---|---|
| `bootstrap/` | O Argo CD (valores do chart) e a Application raiz, aplicados uma vez à mão |
| `apps/` | Uma Application por peça (app of apps); a ordem vem das *sync waves* |
| `platform/traefik/` | O Traefik do k3s: HTTP vira HTTPS |
| `platform/sealed-secrets/` | O controller do Sealed Secrets, do release oficial (versão fixa) |
| `platform/base/` | Namespace `compra-facil` (pods em *restricted*), emissores do Let's Encrypt, o certificado da API e os middlewares do gateway |
| `platform/cnpg-barman/` | O plugin Barman Cloud do CloudNativePG (o operador vem do chart, em `apps/cloudnative-pg.yaml`) |
| `platform/data/` | Os dados: Postgres (CloudNativePG, com backup no Object Storage), Redpanda e Redis |

## Os dados

- **Postgres**: um `Cluster` do CloudNativePG, uma instância, um banco e um usuário por serviço
  (database-per-service, como no compose). As senhas são os Secrets `pg-<serviço>` (selados); cada
  usuário só entra no próprio banco (`pg_hba`). Backup contínuo no bucket `compra-facil-backups`: o WAL
  a cada segmento e uma base por dia, 30 dias para voltar a qualquer instante. Como restaurar:
  [docs/runbooks/postgres-restauracao.md](../../../docs/runbooks/postgres-restauracao.md).
- **Redpanda**: um broker, o log num volume, em modo de produção (com fsync) e sem criação automática
  de tópico (cada serviço cria os seus). O que ainda não foi confirmado pelo broker espera no outbox.
- **Redis**: só o cache do catalog, sem volume: perdê-lo custa uma leitura a mais no banco.
- Os três só aceitam conexão de pods do namespace `compra-facil` (NetworkPolicy).

## Os middlewares do gateway

Os mesmos do compose (`infra/gateway/traefik`), agora como CRDs do Traefik, ligados às rotas de cada
serviço pelo chart (`route` em `infra/k8s/helm/compra-facil-service`):

- `security-headers` (com HSTS), `cors` (a origem da loja) e `strip-bff-key` em todas as rotas;
- `catalog-rate-limit` e `identity-rate-limit` (login e cadastro), por IP;
- as variantes `-bff`, por comprador (`X-Client-IP`), só nas rotas com o segredo do BFF (#176). No
  compose a checagem do segredo é a própria regra do Traefik; aqui ela ficaria no git, então o
  `bff-key` (um Caddy mínimo) confere o `X-Bff-Key` contra um Secret e o Traefik pergunta a ele
  (`forwardAuth`). Chave errada: 403;
- `catalog-problem-429` e `identity-problem-429`: o 429 em ProblemDetails, servido pelo próprio serviço.

O DNS da API fica **sem o proxy da Cloudflare** ("DNS only"): o IP que chega ao Traefik é o do
comprador, que é o que o rate limit precisa, sem confiar em `X-Forwarded-For`.

## Primeira vez

Depois do `tofu apply` (#157) e do DNS apontado:

```bash
scripts/deploy/definir-dominio.sh comprafacil.dev        # troca comprafacil.example em tudo; faça commit
scripts/deploy/definir-object-storage.sh "$(tofu -chdir=infra/oci output -raw s3_endpoint)"   # idem

ssh -fN -L 6443:localhost:6443 ubuntu@<ip>               # túnel para a API do k3s
export KUBECONFIG=~/.kube/compra-facil.yaml              # o k3s.yaml copiado da VM (ver infra/oci)

helm repo add argo https://argoproj.github.io/argo-helm
helm install argocd argo/argo-cd --version 10.11.6 -n argocd --create-namespace \
  -f infra/k8s/producao/bootstrap/argocd-values.yaml --wait
kubectl apply -f infra/k8s/producao/bootstrap/root.yaml  # daí em diante, o Argo CD aplica o resto
```

Os segredos dependem do controller do Sealed Secrets, que o Argo CD acabou de subir:

```bash
cp scripts/deploy/segredos.env.example ~/.compra-facil/segredos.env   # fora do repositório; preencha
scripts/deploy/selar-segredos.sh ~/.compra-facil/segredos.env         # gera platform/base/secrets/*.yaml
git add infra/k8s/producao/platform/base/secrets && git commit        # selados: podem ir para o git
```

**Guarde a chave do Sealed Secrets** fora do cluster: recriado o cluster sem ela, os selados do git não
abrem mais.

```bash
kubectl -n kube-system get secret -l sealedsecrets.bitnami.com/sealed-secrets-key -o yaml > ~/.compra-facil/sealed-secrets-key.yaml
# num cluster novo, antes do Argo CD: kubectl apply -f ~/.compra-facil/sealed-secrets-key.yaml
```

O certificado sai do Let's Encrypt de produção. Para testar sem gastar o limite, troque o `issuerRef` de
`platform/base/certificate.yaml` para `letsencrypt-staging` antes.

## Verificado num k3d com a mesma versão do k3s

Com o cert-manager, o Sealed Secrets, a plataforma e um serviço de teste publicado pelo chart com as
rotas de produção:

- HTTPS com o certificado do cert-manager, HSTS e os cabeçalhos; HTTP responde 301 para HTTPS;
- o prefixo sai antes do serviço (`/identity/v1/me` chega como `/v1/me`);
- 40 logins de uma vez pelo mesmo IP: 20 passam e 20 levam 429;
- pelo BFF, o comprador A esgota o limite dele e o B, logo depois, passa; chave errada dá 403; o
  `X-Client-IP` forjado sem a chave continua limitado pela origem; o `X-Bff-Key` não chega ao serviço;
- o segredo selado pelo `selar-segredos.sh` foi aberto pelo controller;
- o Argo CD sobe com estes valores, e as Applications passam no dry-run do servidor.

E os dados, com um S3 local (SeaweedFS) no lugar do Object Storage:

- o operador e o plugin sobem; o `Cluster` fica saudável com a primeira base no bucket e o WAL
  arquivando; os seis usuários entram cada um no seu banco e são recusados nos outros; o `catalog`
  cria `pg_trgm` e `unaccent`;
- PITR: um cluster ao lado, restaurado num instante, tem a linha que depois foi apagada no original;
- desastre: apagados o cluster e o volume, o `postgres` volta do bucket com o mesmo nome, até o último
  WAL, e segue arquivando com o `serverName` novo (o runbook);
- o Redpanda parte em modo de produção, com `auto_create_topics_enabled=false`, e produz e consome;
- de fora do namespace, Postgres, Redpanda e Redis não respondem.
