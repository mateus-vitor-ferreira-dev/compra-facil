# Infraestrutura na Oracle Cloud (OpenTofu)

A produção do CompraFácil (F9) roda numa VM ARM do **Always Free** da Oracle, com k3s. Este módulo cria
e recria tudo a partir do repositório (#157):

| Arquivo | O que cria |
|---|---|
| `network.tf` | VCN, sub-rede pública, internet gateway e o firewall: 80 e 443 da internet, SSH só de `admin_cidr`. A API do Kubernetes não é exposta (túnel SSH). |
| `compute.tf` | A VM `VM.Standard.A1.Flex` (4 OCPU, 24 GB, 200 GB de disco), Ubuntu 24.04 ARM, e um **IP público reservado**: recriar a VM não muda o DNS. |
| `cloud-init.yaml` | O primeiro boot: atualiza o sistema, abre 80/443 no iptables da imagem da Oracle (que recusa tudo menos o SSH) e instala o k3s numa versão fixa, com os Secrets criptografados no etcd. |
| `storage.tf` | Os buckets de backup do Postgres (privado, versionado) e de mídia (leitura pública, com a CDN na frente), e a chave S3 compatível que o cluster usa para eles. |

O que roda dentro do cluster (cert-manager, Argo CD, os serviços) vem por GitOps (#159, #161).

## Primeira vez (depois da #156)

1. **Chave da API:** no console, *Perfil → Chaves de API → Adicionar*. Guarde o PEM e anote o
   fingerprint, o OCID do usuário, o do tenancy e a região (uma com ARM disponível, ex.: `sa-saopaulo-1`).
2. **Bucket do estado** (o estado não pode morar num bucket que ele mesmo cria):
   *Object Storage → Criar bucket* `compra-facil-tofu-state`, privado e versionado.
3. **Chave S3 do estado:** *Perfil → Chaves secretas do cliente → Gerar*. São o `AWS_ACCESS_KEY_ID` e o
   `AWS_SECRET_ACCESS_KEY` do backend.
4. **No GitHub** (*Settings → Secrets and variables → Actions*):

   | Segredo | Valor |
   |---|---|
   | `OCI_TENANCY_OCID`, `OCI_USER_OCID`, `OCI_COMPARTMENT_OCID` | os OCIDs (o compartimento pode ser o tenancy) |
   | `OCI_FINGERPRINT` | o fingerprint da chave da API |
   | `OCI_PRIVATE_KEY` | o conteúdo do PEM |
   | `TOFU_STATE_ACCESS_KEY`, `TOFU_STATE_SECRET_KEY` | a chave S3 do passo 3 |

   | Variável | Valor |
   |---|---|
   | `OCI_REGION` | ex.: `sa-saopaulo-1` |
   | `OCI_NAMESPACE` | o namespace do Object Storage (aparece no detalhe do bucket) |
   | `SSH_PUBLIC_KEY` | a sua chave pública |
   | `ADMIN_CIDR` | o seu IP, ex.: `203.0.113.10/32` |
   | `DOMAIN` | o domínio da loja |

5. **Environment `producao`** (*Settings → Environments*), com você como *required reviewer*: o apply
   espera a sua aprovação.
6. **Aplicar:** *Actions → infra → Run workflow* com `apply` marcado. O plano sai no resumo do job.
7. **DNS** (Cloudflare): registros A de `api.`, `grafana.` e `k3s.` para o `public_ip` da saída.

## Pela máquina (alternativa ao CI)

```bash
cd infra/oci
cp backend.hcl.example backend.hcl            # e preencha o namespace e a região
cp terraform.tfvars.example terraform.tfvars  # e preencha
export AWS_ACCESS_KEY_ID=... AWS_SECRET_ACCESS_KEY=...
tofu init -backend-config=backend.hcl
tofu plan
tofu apply
tofu output -raw s3_secret_access_key         # vai para o Secret dos backups (#160)
```

## Acesso ao cluster

```bash
ssh -L 6443:localhost:6443 ubuntu@<public_ip>                 # túnel para a API do Kubernetes
ssh ubuntu@<public_ip> sudo cat /etc/rancher/k3s/k3s.yaml > ~/.kube/compra-facil.yaml
KUBECONFIG=~/.kube/compra-facil.yaml kubectl get nodes        # pelo túnel, em 127.0.0.1:6443
```

## Decisões

- **Uma VM, não um cluster gerenciado:** o OKE no Always Free cobra o plano de controle; o k3s numa VM
  ARM de 4 OCPU e 24 GB cabe todo o sistema com folga e custa zero.
- **IP reservado:** a VM pode ser recriada (imagem nova, troca de domínio de disponibilidade) sem mexer
  no DNS.
- **`ignore_changes` na imagem e no cloud-init:** uma imagem nova da Oracle não derruba a produção num
  `apply` qualquer; atualizar a VM é uma decisão explícita (`tofu apply -replace=oci_core_instance.k3s`).
- **Sem capacidade ARM:** é comum no Always Free. Troque `availability_domain_index` ou tente de novo
  mais tarde; a Oracle libera capacidade ao longo do dia.
