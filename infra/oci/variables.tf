# Credenciais da API da Oracle (usuário com chave de API). No CI vêm dos segredos do GitHub (TF_VAR_*).
variable "tenancy_ocid" {
  type = string
}

variable "user_ocid" {
  type = string
}

variable "fingerprint" {
  type = string
}

variable "private_key_path" {
  type        = string
  description = "Arquivo da chave privada da API (PEM)."
}

variable "region" {
  type        = string
  description = "Região com capacidade ARM no Always Free (ex.: sa-saopaulo-1, sa-vinhedo-1)."
}

variable "compartment_ocid" {
  type        = string
  description = "Compartimento dos recursos; o da raiz (o tenancy) serve."
}

variable "availability_domain_index" {
  type        = number
  default     = 0
  description = "Qual domínio de disponibilidade usar: quando um está sem ARM, troque o índice."
}

variable "ssh_public_key" {
  type        = string
  description = "Chave pública para o SSH na VM (usuário ubuntu)."
}

variable "admin_cidr" {
  type        = string
  description = "De onde o SSH é aceito (o seu IP, ex.: 203.0.113.10/32). A API do Kubernetes não é exposta: usa túnel SSH."

  validation {
    condition     = can(cidrhost(var.admin_cidr, 0)) && var.admin_cidr != "0.0.0.0/0"
    error_message = "Use um CIDR válido e restrito; o SSH não fica aberto para a internet inteira."
  }
}

variable "domain" {
  type        = string
  description = "Domínio da loja (ex.: comprafacil.dev); entra no certificado da API do k3s."
}

variable "ocpus" {
  type        = number
  default     = 4
  description = "OCPUs ARM (o Always Free dá até 4 no total)."
}

variable "memory_gbs" {
  type    = number
  default = 24
}

variable "boot_volume_gbs" {
  type        = number
  default     = 200
  description = "O Always Free dá até 200 GB de bloco no total."
}

variable "k3s_version" {
  type        = string
  default     = "v1.36.5+k3s1"
  description = "Versão do k3s, fixa: a VM recriada sobe a mesma."
}
