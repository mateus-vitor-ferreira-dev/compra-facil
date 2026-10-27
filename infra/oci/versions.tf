# Infraestrutura na Oracle Cloud (F9, #157): uma VM ARM do Always Free com k3s, a rede dela e os
# buckets de backup e de mídia. O estado fica no Object Storage, pelo backend S3 compatível da Oracle
# (configuração em backend.hcl, fora do repositório; modelo em backend.hcl.example).
terraform {
  required_version = ">= 1.10"

  required_providers {
    oci = {
      source  = "oracle/oci"
      version = "~> 9.11"
    }
  }

  backend "s3" {}
}

provider "oci" {
  tenancy_ocid     = var.tenancy_ocid
  user_ocid        = var.user_ocid
  fingerprint      = var.fingerprint
  private_key_path = var.private_key_path
  region           = var.region
}
