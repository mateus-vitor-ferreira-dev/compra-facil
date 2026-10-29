package openapi

import (
	"os"
	"path/filepath"
	"testing"

	"github.com/stretchr/testify/assert"
	"github.com/stretchr/testify/require"
)

const contractsDir = "../../../../../contracts/openapi"

func TestEmbeddedContractInSync(t *testing.T) {
	if _, err := os.Stat(contractsDir); os.IsNotExist(err) {
		t.Skip("contracts/ fora do contexto (ex.: build da imagem)")
	}
	for _, name := range []string{Spec, "common/v1/common.yaml", "common/v1/dead-letters.yaml"} {
		embedded, err := Files.ReadFile(name)
		require.NoError(t, err)
		source, err := os.ReadFile(filepath.Join(contractsDir, name))
		require.NoError(t, err)
		assert.Equal(t, string(source), string(embedded), "%s desatualizado: rode go generate ./api/openapi", name)
	}
}
