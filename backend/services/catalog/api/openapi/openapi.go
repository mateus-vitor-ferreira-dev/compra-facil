// Package openapi embute o contrato catalog.v1 (cópia de contracts/openapi) para o Swagger UI e os testes.
//
// A fonte da verdade é contracts/openapi; atualize a cópia com `go generate ./api/openapi`.
// O teste TestEmbeddedContractInSync falha se as cópias divergirem.
package openapi

import "embed"

//go:generate cp ../../../../../contracts/openapi/catalog.v1.yaml catalog.v1.yaml
//go:generate cp ../../../../../contracts/openapi/common/v1/common.yaml common/v1/common.yaml

// Files contém catalog.v1.yaml e common/v1/common.yaml, com os mesmos caminhos relativos do $ref.
//
//go:embed catalog.v1.yaml common/v1/common.yaml
var Files embed.FS

const Spec = "catalog.v1.yaml"
