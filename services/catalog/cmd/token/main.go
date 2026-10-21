// Comando token emite um JWT de operador para o ambiente local (não há serviço de identidade).
//
//	go run ./cmd/token                      # admin, válido por 12h
//	go run ./cmd/token -sub ana -ttl 1h -roles admin
//
// Usa o mesmo auth.jwt_secret/issuer/audience da api (config.yaml ou CATALOG_AUTH_*).
package main

import (
	"flag"
	"fmt"
	"os"
	"strings"
	"time"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/auth"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/services/catalog/internal/config"
)

func main() {
	sub := flag.String("sub", "operador-local", "subject do token (aparece nos logs de auditoria)")
	roles := flag.String("roles", auth.RoleAdmin, "roles separadas por vírgula")
	ttl := flag.Duration("ttl", 12*time.Hour, "validade do token")
	flag.Parse()

	cfg, err := config.Load("./config", ".")
	if err != nil {
		fmt.Fprintln(os.Stderr, "token: config:", err)
		os.Exit(1)
	}
	tok, err := auth.Issue(cfg.Auth.Options(), *sub, strings.Split(*roles, ","), *ttl, time.Now())
	if err != nil {
		fmt.Fprintln(os.Stderr, "token:", err)
		os.Exit(1)
	}
	fmt.Println(tok)
}
