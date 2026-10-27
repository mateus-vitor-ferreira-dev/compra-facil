data "oci_objectstorage_namespace" "ns" {
  compartment_id = var.tenancy_ocid
}

# backups do Postgres (CloudNativePG, #160): privado, versionado (um backup sobrescrito por engano volta)
resource "oci_objectstorage_bucket" "backups" {
  compartment_id = var.compartment_ocid
  namespace      = data.oci_objectstorage_namespace.ns.namespace
  name           = "compra-facil-backups"
  access_type    = "NoPublicAccess"
  versioning     = "Enabled"
}

# fotos dos produtos: leitura pública, com a CDN da Cloudflare na frente (cache longo, nomes com hash)
resource "oci_objectstorage_bucket" "media" {
  compartment_id = var.compartment_ocid
  namespace      = data.oci_objectstorage_namespace.ns.namespace
  name           = "compra-facil-media"
  access_type    = "ObjectRead"
}

# chave S3 compatível para o barman (backups do CloudNativePG) e o upload das fotos; o segredo só sai
# como output sensível, e vira um Secret selado no cluster
resource "oci_identity_customer_secret_key" "s3" {
  user_id      = var.user_ocid
  display_name = "compra-facil-s3"
}
