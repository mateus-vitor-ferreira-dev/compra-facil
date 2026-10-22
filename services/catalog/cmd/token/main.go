// Comando token emite um JWT de operador para o ambiente local (não há serviço de identidade).
//
//	go run ./cmd/token                      # admin, válido por 12h
//	go run ./cmd/token -sub ana -ttl 1h -roles admin
//	go run ./cmd/token -aud inventory         # para as rotas admin de outro serviço
//
// Usa o mesmo auth.jwt_secret/issuer da api (config.yaml ou CATALOG_AUTH_*). A audiência padrão é
// a do catalog; os serviços .NET aceitam o mesmo segredo com a audiência = nome do serviço.
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
	aud := flag.String("aud", "", "audiência (serviço de destino); vazio = a do catalog")
	flag.Parse()

	cfg, err := config.Load("./config", ".")
	if err != nil {
		fmt.Fprintln(os.Stderr, "token: config:", err)
		os.Exit(1)
	}
	opts := cfg.Auth.Options()
	if *aud != "" {
		opts.Audience = *aud
	}
	tok, err := auth.Issue(opts, *sub, strings.Split(*roles, ","), *ttl, time.Now())
	if err != nil {
		fmt.Fprintln(os.Stderr, "token:", err)
		os.Exit(1)
	}
	fmt.Println(tok)
}
