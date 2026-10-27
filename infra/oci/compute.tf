data "oci_identity_availability_domains" "all" {
  compartment_id = var.tenancy_ocid
}

# a imagem Ubuntu 24.04 ARM mais recente da Oracle
data "oci_core_images" "ubuntu" {
  compartment_id           = var.compartment_ocid
  operating_system         = "Canonical Ubuntu"
  operating_system_version = "24.04"
  shape                    = "VM.Standard.A1.Flex"
  sort_by                  = "TIMECREATED"
  sort_order               = "DESC"
}

resource "oci_core_instance" "k3s" {
  compartment_id      = var.compartment_ocid
  availability_domain = data.oci_identity_availability_domains.all.availability_domains[var.availability_domain_index].name
  display_name        = "compra-facil-k3s"
  shape               = "VM.Standard.A1.Flex"

  shape_config {
    ocpus         = var.ocpus
    memory_in_gbs = var.memory_gbs
  }

  source_details {
    source_type             = "image"
    source_id               = data.oci_core_images.ubuntu.images[0].id
    boot_volume_size_in_gbs = var.boot_volume_gbs
  }

  create_vnic_details {
    subnet_id = oci_core_subnet.public.id
    # o IP público é o reservado (abaixo): sobrevive a recriar a VM, e o DNS não muda
    assign_public_ip = false
    hostname_label   = "k3s"
  }

  metadata = {
    ssh_authorized_keys = var.ssh_public_key
    user_data = base64encode(templatefile("${path.module}/cloud-init.yaml", {
      k3s_version = var.k3s_version
      domain      = var.domain
    }))
  }

  lifecycle {
    # imagem nova da Oracle não recria a VM sozinha: atualizar é decisão de quem opera
    ignore_changes = [source_details[0].source_id, metadata["user_data"]]
  }
}

data "oci_core_vnic_attachments" "k3s" {
  compartment_id = var.compartment_ocid
  instance_id    = oci_core_instance.k3s.id
}

data "oci_core_private_ips" "k3s" {
  vnic_id = data.oci_core_vnic_attachments.k3s.vnic_attachments[0].vnic_id
}

resource "oci_core_public_ip" "k3s" {
  compartment_id = var.compartment_ocid
  lifetime       = "RESERVED"
  display_name   = "compra-facil-k3s"
  private_ip_id  = data.oci_core_private_ips.k3s.private_ips[0].id
}
