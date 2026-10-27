output "public_ip" {
  description = "O IP reservado da VM: os registros A de api., grafana. e do domínio apontam para ele."
  value       = oci_core_public_ip.k3s.ip_address
}

output "ssh" {
  description = "Acesso à VM; a API do Kubernetes vem por túnel (ssh -L 6443:localhost:6443 ...)."
  value       = "ssh ubuntu@${oci_core_public_ip.k3s.ip_address}"
}

output "s3_endpoint" {
  description = "Endpoint S3 compatível do Object Storage (backups e mídia)."
  value       = "https://${data.oci_objectstorage_namespace.ns.namespace}.compat.objectstorage.${var.region}.oraclecloud.com"
}

output "buckets" {
  value = {
    backups = oci_objectstorage_bucket.backups.name
    media   = oci_objectstorage_bucket.media.name
  }
}

output "s3_access_key_id" {
  value = oci_identity_customer_secret_key.s3.id
}

output "s3_secret_access_key" {
  value     = oci_identity_customer_secret_key.s3.key
  sensitive = true
}
