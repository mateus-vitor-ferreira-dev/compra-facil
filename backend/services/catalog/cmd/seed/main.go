// Comando seed popula o banco do catálogo com massa de dados (categorias, produtos e campanhas).
//
//	go run ./cmd/seed                    # 10 mil produtos; recusa se já houver produtos
//	go run ./cmd/seed -reset -products 50000
//
// É determinístico para o mesmo -seed: rodadas de k6 comparáveis usam os mesmos dados.
//
// Grava também um ProductCreated por produto e um CampaignCreated por campanha no outbox, na mesma
// transação: o relay do catalog publica e os outros serviços (inventory, orders) passam a conhecer
// a massa. O -reset não publica remoções: quem consome guarda os dados antigos até o reset dele.
package main

import (
	"context"
	"encoding/binary"
	"encoding/json"
	"errors"
	"flag"
	"fmt"
	"math/rand/v2"
	"os"
	"time"

	"github.com/google/uuid"
	"github.com/jackc/pgx/v5"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/config"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/events"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/migrations"
)

type options struct {
	products int
	seed     uint64
	reset    bool
	mediaURL string
}

func main() {
	var opts options
	flag.IntVar(&opts.products, "products", 10_000, "quantidade de produtos")
	flag.Uint64Var(&opts.seed, "seed", 42, "semente do gerador (mesma semente, mesmos dados)")
	flag.BoolVar(&opts.reset, "reset", false, "apaga o catálogo antes de popular")
	flag.StringVar(&opts.mediaURL, "media-url", envOr("CATALOG_SEED_MEDIA_URL", "http://localhost/media"),
		"base das fotos dos produtos, como o navegador as vê (gateway /media ou a CDN)")
	flag.Parse()

	if err := run(context.Background(), opts); err != nil {
		fmt.Fprintln(os.Stderr, "seed:", err)
		os.Exit(1)
	}
}

func envOr(key, fallback string) string {
	if v := os.Getenv(key); v != "" {
		return v
	}
	return fallback
}

func run(ctx context.Context, opts options) error {
	if opts.products < 1 {
		return errors.New("-products deve ser maior que zero")
	}
	cfg, err := config.Load("./config", ".")
	if err != nil {
		return fmt.Errorf("config: %w", err)
	}
	if err := migrations.Up(cfg.DB.DSN); err != nil {
		return fmt.Errorf("migrações: %w", err)
	}
	conn, err := pgx.Connect(ctx, cfg.DB.DSN)
	if err != nil {
		return fmt.Errorf("conectando: %w", err)
	}
	defer func() { _ = conn.Close(ctx) }()

	start := time.Now()
	stats, err := seed(ctx, conn, opts, start.UTC())
	if err != nil {
		return err
	}
	fmt.Printf("seed: %d categorias, %d produtos, %d campanhas (%d vínculos), %d eventos no outbox em %s\n",
		stats.categories, stats.products, stats.campaigns, stats.links, stats.events, time.Since(start).Round(time.Millisecond))
	return nil
}

type stats struct{ categories, products, campaigns, links, events int }

func seed(ctx context.Context, conn *pgx.Conn, opts options, now time.Time) (stats, error) {
	var st stats
	rng := rand.New(rand.NewPCG(opts.seed, opts.seed^0x9e3779b97f4a7c15)) //nolint:gosec // dados de teste

	tx, err := conn.Begin(ctx)
	if err != nil {
		return st, fmt.Errorf("abrindo transação: %w", err)
	}
	defer func() { _ = tx.Rollback(ctx) }()

	if err := prepare(ctx, tx, opts.reset); err != nil {
		return st, err
	}

	subs, n, err := insertCategories(ctx, tx, rng)
	if err != nil {
		return st, err
	}
	st.categories = n

	products := generateProducts(rng, subs, opts.products, now, opts.mediaURL)
	if _, err := tx.CopyFrom(ctx, pgx.Identifier{"products"},
		[]string{"id", "sku", "name", "description", "category_id", "image_url", "image", "price_cents", "currency", "status", "created_at", "updated_at"},
		pgx.CopyFromSlice(len(products), func(i int) ([]any, error) { return products[i].row(), nil }),
	); err != nil {
		return st, fmt.Errorf("copiando produtos: %w", err)
	}
	st.products = len(products)

	campaigns, err := insertCampaigns(ctx, tx, rng, products, now)
	if err != nil {
		return st, err
	}
	st.campaigns = len(campaigns)
	for _, c := range campaigns {
		st.links += len(c.productIDs)
	}

	msgs, err := seedEvents(ctx, products, campaigns, now)
	if err != nil {
		return st, err
	}
	if err := copyOutbox(ctx, tx, msgs); err != nil {
		return st, err
	}
	st.events = len(msgs)

	if err := tx.Commit(ctx); err != nil {
		return st, fmt.Errorf("commit: %w", err)
	}

	// Sem isto, a pending list do GIN deixa a busca trigram em seq scan logo após a carga (ver #71).
	if _, err := conn.Exec(ctx, "VACUUM ANALYZE categories, products, campaigns, campaign_products"); err != nil {
		return st, fmt.Errorf("vacuum: %w", err)
	}
	return st, nil
}

func prepare(ctx context.Context, tx pgx.Tx, reset bool) error {
	if reset {
		// o outbox junto: eventos ainda não publicados seriam de produtos que deixam de existir
		if _, err := tx.Exec(ctx, "TRUNCATE outbox, campaign_products, campaigns, products, categories CASCADE"); err != nil {
			return fmt.Errorf("limpando catálogo: %w", err)
		}
		return nil
	}
	var count int
	if err := tx.QueryRow(ctx, "SELECT count(*) FROM products").Scan(&count); err != nil {
		return fmt.Errorf("contando produtos: %w", err)
	}
	if count > 0 {
		return fmt.Errorf("o catálogo já tem %d produtos; use -reset para recriar", count)
	}
	return nil
}

type seededSub struct {
	subcategory
	id       uuid.UUID
	rootSlug string
}

func insertCategories(ctx context.Context, tx pgx.Tx, rng *rand.Rand) ([]seededSub, int, error) {
	var subs []seededSub
	count := 0
	for _, root := range catalogTree {
		rootID := newUUID(rng)
		if _, err := tx.Exec(ctx, "INSERT INTO categories (id, slug, name) VALUES ($1, $2, $3)", rootID, root.slug, root.name); err != nil {
			return nil, 0, fmt.Errorf("categoria %s: %w", root.slug, err)
		}
		count++
		for _, sub := range root.children {
			id := newUUID(rng)
			if _, err := tx.Exec(ctx, "INSERT INTO categories (id, slug, name, parent_id) VALUES ($1, $2, $3, $4)",
				id, sub.slug, sub.name, rootID); err != nil {
				return nil, 0, fmt.Errorf("categoria %s: %w", sub.slug, err)
			}
			count++
			subs = append(subs, seededSub{subcategory: sub, id: id, rootSlug: root.slug})
		}
	}
	return subs, count, nil
}

type seedProduct struct {
	id         uuid.UUID
	sku, name  string
	desc       string
	categoryID uuid.UUID
	rootSlug   string
	imageURL   string
	image      *domain.ProductImage
	price      int64
	status     string
	createdAt  time.Time
}

// domain é o produto como o repositório o devolveria logo após o Create (versão 1).
func (p seedProduct) domain() domain.Product {
	return domain.Product{
		ID: p.id, SKU: p.sku, Name: p.name, Description: p.desc, CategoryID: p.categoryID, ImageURL: &p.imageURL, Image: p.image,
		PriceCents: p.price, Currency: "BRL",
		Status: domain.ProductStatus(p.status), Version: 1, CreatedAt: p.createdAt, UpdatedAt: p.createdAt,
	}
}

func (p seedProduct) row() []any {
	image, _ := json.Marshal(p.image) // tipos simples: não falha
	return []any{p.id, p.sku, p.name, p.desc, p.categoryID, p.imageURL, image, p.price, "BRL", p.status, p.createdAt, p.createdAt}
}

func generateProducts(rng *rand.Rand, subs []seededSub, n int, now time.Time, mediaURL string) []seedProduct {
	photos, err := loadPhotos()
	if err != nil {
		panic(err) // embutido no binário: só falha se o arquivo versionado estiver quebrado
	}
	out := make([]seedProduct, n)
	for i := range out {
		sub := subs[i%len(subs)] // distribuição uniforme entre subcategorias
		noun := pick(rng, sub.nouns)
		brand := pick(rng, sub.brands)
		attr := pick(rng, sub.attrs)

		status := "active"
		switch r := rng.Float64(); {
		case r < 0.03:
			status = "draft"
		case r < 0.08:
			status = "inactive"
		}

		sku := fmt.Sprintf("%s-%06d", sub.skuPrefix, i+1)
		imageURL, image := photoFor(rng, photos, mediaURL, sub.slug)
		out[i] = seedProduct{
			id:         newUUID(rng),
			sku:        sku,
			name:       fmt.Sprintf("%s %s %s", noun, brand, attr),
			desc:       fmt.Sprintf("%s %s %s. Garantia de 12 meses e entrega para todo o Brasil.", noun, brand, attr),
			categoryID: sub.id,
			rootSlug:   sub.rootSlug,
			imageURL:   imageURL,
			image:      image,
			price:      roundPrice(sub.minPrice + rng.Int64N(sub.maxPrice-sub.minPrice+1)),
			status:     status,
			createdAt:  now.Add(-time.Duration(rng.Int64N(int64(365 * 24 * time.Hour)))),
		}
	}
	return out
}

type seededCampaign struct {
	domain.Campaign
	productIDs []uuid.UUID
}

// insertCampaigns cria as campanhas e distribui produtos ativos sem que um produto fique em duas
// campanhas que se cruzam no tempo (a regra do CampaignService).
func insertCampaigns(ctx context.Context, tx pgx.Tx, rng *rand.Rand, products []seedProduct, now time.Time) ([]seededCampaign, error) {
	active := make([]int, 0, len(products))
	for i, p := range products {
		if p.status == "active" {
			active = append(active, i)
		}
	}
	rng.Shuffle(len(active), func(i, j int) { active[i], active[j] = active[j], active[i] })

	type period struct{ start, end time.Time }
	taken := map[int][]period{} // produto -> períodos em que já está em campanha
	var links [][]any
	out := make([]seededCampaign, 0, len(seedCampaigns))

	for _, sc := range seedCampaigns {
		id := newUUID(rng)
		start := now.AddDate(0, 0, sc.startOffsetDays).Truncate(time.Hour)
		end := start.AddDate(0, 0, sc.durationDays)
		if _, err := tx.Exec(ctx,
			"INSERT INTO campaigns (id, slug, name, discount_percent, starts_at, ends_at) VALUES ($1, $2, $3, $4, $5, $6)",
			id, sc.slug, sc.name, sc.discount, start, end); err != nil {
			return nil, fmt.Errorf("campanha %s: %w", sc.slug, err)
		}
		camp := seededCampaign{Campaign: domain.Campaign{
			ID: id, Slug: sc.slug, Name: sc.name, DiscountPercent: sc.discount, StartsAt: start, EndsAt: end, Version: 1,
		}}

		eligible := active
		if sc.rootSlug != "" {
			eligible = nil
			for _, idx := range active {
				if products[idx].rootSlug == sc.rootSlug {
					eligible = append(eligible, idx)
				}
			}
		}
		want := int(float64(len(eligible)) * sc.share)
		for _, idx := range eligible {
			if want == 0 {
				break
			}
			overlaps := false
			for _, p := range taken[idx] {
				if p.start.Before(end) && p.end.After(start) {
					overlaps = true
					break
				}
			}
			if overlaps {
				continue
			}
			taken[idx] = append(taken[idx], period{start, end})
			links = append(links, []any{id, products[idx].id})
			camp.productIDs = append(camp.productIDs, products[idx].id)
			want--
		}
		out = append(out, camp)
	}

	if _, err := tx.CopyFrom(ctx, pgx.Identifier{"campaign_products"}, []string{"campaign_id", "product_id"},
		pgx.CopyFromRows(links)); err != nil {
		return nil, fmt.Errorf("copiando vínculos de campanha: %w", err)
	}
	return out, nil
}

// seedEvents monta os eventos da carga: produtos antes das campanhas, que os referenciam.
// Todos levam o mesmo correlation_id (um fluxo só: esta rodada do seed).
func seedEvents(ctx context.Context, products []seedProduct, campaigns []seededCampaign, now time.Time) ([]events.Message, error) {
	ctx = events.WithCorrelationID(ctx, "seed-"+uuid.NewString())
	msgs := make([]events.Message, 0, len(products)+len(campaigns))
	for _, p := range products {
		m, err := events.ProductCreated(ctx, p.domain(), now)
		if err != nil {
			return nil, fmt.Errorf("evento do produto %s: %w", p.sku, err)
		}
		msgs = append(msgs, m)
	}
	for _, c := range campaigns {
		m, err := events.CampaignCreated(ctx, c.Campaign, c.productIDs, now)
		if err != nil {
			return nil, fmt.Errorf("evento da campanha %s: %w", c.Slug, err)
		}
		msgs = append(msgs, m)
	}
	return msgs, nil
}

func copyOutbox(ctx context.Context, tx pgx.Tx, msgs []events.Message) error {
	_, err := tx.CopyFrom(ctx, pgx.Identifier{"outbox"},
		[]string{"event_id", "topic", "message_key", "event_type", "payload", "headers"},
		pgx.CopyFromSlice(len(msgs), func(i int) ([]any, error) {
			m := msgs[i]
			return []any{m.EventID, m.Topic, m.Key, m.Type, string(m.Payload), "{}"}, nil
		}))
	if err != nil {
		return fmt.Errorf("copiando eventos para o outbox: %w", err)
	}
	return nil
}

// newUUID tira o UUID (v4) do gerador semeado: mesma semente, mesmos ids entre rodadas.
func newUUID(rng *rand.Rand) uuid.UUID {
	var id uuid.UUID
	for i := 0; i < len(id); i += 8 {
		binary.LittleEndian.PutUint64(id[i:], rng.Uint64())
	}
	id[6] = id[6]&0x0f | 0x40 // versão 4
	id[8] = id[8]&0x3f | 0x80 // variante RFC 4122
	return id
}

func pick[T any](rng *rand.Rand, xs []T) T { return xs[rng.IntN(len(xs))] }

// roundPrice termina o preço em ,90 (R$ 1.234,90), como numa vitrine de verdade.
func roundPrice(cents int64) int64 {
	return cents/100*100 + 90
}
