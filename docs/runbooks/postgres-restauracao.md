# Restaurar o Postgres de produção

O Postgres da produção (`infra/k8s/producao/platform/data`) é um `Cluster` do CloudNativePG com backup
contínuo no Object Storage da Oracle (bucket `compra-facil-backups`, plugin Barman Cloud):

- o WAL vai para o bucket a cada segmento fechado (no máximo uns 5 minutos de atraso com a loja parada,
  `archive_timeout` do operador);
- uma base por dia, às 3h de Brasília (`ScheduledBackup postgres-diario`), e 30 dias de retenção;
- com a base e o WAL dá para voltar a **qualquer instante** dos últimos 30 dias (PITR).

Os dois casos abaixo foram ensaiados num k3d com um S3 local no lugar do Object Storage.

Antes de tudo:

```bash
ssh -fN -L 6443:localhost:6443 ubuntu@<ip>
export KUBECONFIG=~/.kube/compra-facil.yaml
kubectl -n compra-facil get cluster postgres     # "Cluster in healthy state"?
kubectl -n compra-facil get backup               # a última base "completed"
kubectl -n compra-facil get cluster postgres \
  -o jsonpath='{.status.conditions[?(@.type=="ContinuousArchiving")].message}'
```

## Caso 1: um dado sumiu ou foi estragado (o banco está de pé)

Um deploy apagou linhas, um comando errado no painel. O banco segue no ar; a restauração vai para um
**cluster ao lado**, no instante de antes do estrago, e de lá se copia o que falta. O cluster de
produção não é tocado.

1. Descubra o instante (UTC) de antes do problema: o log do serviço no Grafana, o horário do deploy.
2. Crie o cluster restaurado:

   ```yaml
   # restaurado.yaml (não vai para o git: é temporário)
   apiVersion: postgresql.cnpg.io/v1
   kind: Cluster
   metadata:
     name: postgres-restaurado
     namespace: compra-facil
   spec:
     instances: 1
     imageName: ghcr.io/cloudnative-pg/postgresql:17.11-standard-trixie
     storage: { size: 20Gi }
     bootstrap:
       recovery:
         source: origem
         recoveryTarget:
           targetTime: "2026-10-27 14:36:18+00"   # o instante de antes do estrago
     externalClusters:
       - name: origem
         plugin:
           name: barman-cloud.cloudnative-pg.io
           parameters:
             barmanObjectName: backups
             serverName: postgres          # o serverName que o cluster de produção usa hoje
   ```

   Sem `plugins` no restaurado: ele **não** arquiva WAL, então não mistura nada no bucket.

   ```bash
   kubectl apply -f restaurado.yaml
   kubectl -n compra-facil wait cluster/postgres-restaurado --for=condition=Ready --timeout=30m
   ```

3. Confira e copie o que falta (exemplo: os pedidos de um dia):

   ```bash
   kubectl -n compra-facil exec postgres-restaurado-1 -c postgres -- \
     pg_dump -d orders --data-only -t '<tabela>' > recuperado.sql
   # revise o arquivo, e aplique no de produção com o usuário do serviço
   ```

4. Apague o restaurado: `kubectl -n compra-facil delete cluster postgres-restaurado`.

## Caso 2: o cluster ou o volume se perdeu

A VM foi recriada, o disco corrompeu, o PVC foi apagado. O cluster `postgres` é recriado **com o
mesmo nome** a partir do bucket, e os serviços, que falam com `postgres-rw`, voltam sem mudar nada.

O Argo CD recriaria o `postgres` sozinho a partir do git, mas **vazio** (`initdb`). Por isso o
primeiro passo é parar o sync automático.

1. Pare o Argo CD (a raiz primeiro: ela reescreveria a política da Application `data`):

   ```bash
   for app in compra-facil data; do
     kubectl -n argocd patch application $app --type merge -p '{"spec":{"syncPolicy":{"automated":null}}}'
   done
   ```

2. Se sobrou algo do cluster quebrado, apague (o bucket não é tocado):

   ```bash
   kubectl -n compra-facil delete cluster postgres --wait
   kubectl -n compra-facil delete pvc -l cnpg.io/cluster=postgres
   ```

3. No git, em `infra/k8s/producao/platform/data/postgres.yaml`, troque o `bootstrap` pela
   recuperação e dê ao arquivo de WAL um **nome novo** (o antigo é a fonte; o cluster novo não pode
   escrever por cima dele). Se o `serverName` atual já for `postgres-r1`, a fonte é `postgres-r1` e o
   novo `postgres-r2`, e assim por diante:

   ```yaml
   bootstrap:
     recovery:
       source: origem
       database: catalog
       owner: catalog
       secret: { name: pg-catalog }
       # recoveryTarget: { targetTime: "..." }   # só se for voltar para antes de um estrago;
                                                 # sem ele, vai até o último WAL arquivado
   externalClusters:
     - name: origem
       plugin:
         name: barman-cloud.cloudnative-pg.io
         parameters:
           barmanObjectName: backups
           serverName: postgres
   plugins:
     - name: barman-cloud.cloudnative-pg.io
       isWALArchiver: true
       parameters:
         barmanObjectName: backups
         serverName: postgres-r1
   ```

   Commit e merge na `main`. Esse bloco fica no git depois da recuperação: o `bootstrap` só vale na
   criação, e o `serverName` novo é para onde o WAL vai daqui em diante.

4. Ligue o Argo CD de novo (a raiz devolve a política da `data`, que estava no git) e espere:

   ```bash
   kubectl -n argocd patch application compra-facil --type merge \
     -p '{"spec":{"syncPolicy":{"automated":{"prune":true,"selfHeal":true}}}}'
   kubectl -n compra-facil wait cluster/postgres --for=condition=Ready --timeout=30m
   ```

5. **Tire uma base agora.** O WAL novo (`postgres-r1`) sem uma base não restaura nada, e a próxima
   do agendamento só sai às 3h:

   ```yaml
   apiVersion: postgresql.cnpg.io/v1
   kind: Backup
   metadata:
     name: postgres-pos-recuperacao
     namespace: compra-facil
   spec:
     cluster: { name: postgres }
     method: plugin
     pluginConfiguration: { name: barman-cloud.cloudnative-pg.io }
   ```

   ```bash
   kubectl apply -f backup.yaml
   kubectl -n compra-facil wait backup/postgres-pos-recuperacao --for=jsonpath='{.status.phase}'=completed --timeout=30m
   ```

6. Confira: `ContinuousArchiving=True` no cluster, os `Database` com `APPLIED true`
   (`kubectl -n compra-facil get database`), e os serviços respondendo (`/health/ready`).

Os usuários e as senhas voltam com os dados e são conferidos com os Secrets `pg-<serviço>` (os
`managed.roles`); os `Database` se reconciliam sozinhos em um ou dois minutos.

## Se a VM inteira se perdeu

Antes do caso 2: recrie a VM (`infra/oci`), restaure a chave do Sealed Secrets **antes** do Argo CD
(README de `infra/k8s/producao`) e suba o Argo CD. Sem a chave, os Secrets `backup-s3` e `pg-*` não
abrem e a restauração não tem como ler o bucket.
