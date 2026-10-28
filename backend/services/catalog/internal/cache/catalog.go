package cache

import (
	"context"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"log/slog"
	"strconv"
	"time"

	"github.com/google/uuid"
	"go.opentelemetry.io/otel"
	"go.opentelemetry.io/otel/attribute"
	"go.opentelemetry.io/otel/metric"
	"golang.org/x/sync/singleflight"

	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/domain"
	"github.com/mateus-vitor-ferreira-dev/compra-facil/backend/services/catalog/internal/service"
)

// Invalidação por geração: toda chave leva o número da geração atual, e qualquer escrita admin
// incrementa esse número. As chaves antigas deixam de ser lidas e expiram pelo TTL. Escrita é
// rara (time de catálogo) e leitura é o pico, então invalidar tudo sai mais barato e mais seguro
// que rastrear quais listagens contêm qual produto (e campanha muda o preço de muitos de uma vez).
const (
	keyPrefix     = "catalog:v1:"
	generationKey = keyPrefix + "gen"
)

type productReader interface {
	ListPublic(ctx context.Context, in service.ListProductsInput) (domain.Page[domain.Product], error)
	ListBrands(ctx context.Context, in service.ListProductsInput) ([]domain.BrandCount, error)
	ListCategoryCounts(ctx context.Context, in service.ListProductsInput) ([]domain.CategoryCount, error)
	GetPublic(ctx context.Context, id uuid.UUID) (domain.Product, error)
}

type categoryReader interface {
	List(ctx context.Context) ([]domain.Category, error)
}

type campaignReader interface {
	ListActive(ctx context.Context) ([]domain.Campaign, error)
	NextStart(ctx context.Context) (time.Time, error)
}

type Options struct {
	// ProductTTL vale para o detalhe; ListTTL para listagens, categorias e campanhas.
	// Ambos são encurtados para não passar de quando o preço efetivo muda: o fim da campanha que
	// dá o desconto ou o início da próxima campanha.
	ProductTTL time.Duration
	ListTTL    time.Duration
}

// Catalog implementa as leituras públicas (mesmos métodos dos services) com cache-aside.
// Se o Redis falhar, a leitura vai direto ao banco: cache é otimização, não dependência.
type Catalog struct {
	store      Store
	products   productReader
	categories categoryReader
	campaigns  campaignReader
	opts       Options
	now        service.Clock
	log        *slog.Logger
	group      singleflight.Group
	lookups    metric.Int64Counter
}

func NewCatalog(store Store, p productReader, c categoryReader, camp campaignReader, opts Options, now service.Clock, log *slog.Logger) (*Catalog, error) {
	// sem MeterProvider configurado (até a instrumentação do serviço), o global é noop
	lookups, err := otel.Meter("catalog/cache").Int64Counter("catalog.cache.lookups",
		metric.WithDescription("Consultas ao cache por tipo e resultado (hit, miss, error); hit ratio = hit/(hit+miss)."))
	if err != nil {
		return nil, fmt.Errorf("métrica do cache: %w", err)
	}
	return &Catalog{
		store: store, products: p, categories: c, campaigns: camp,
		opts: opts, now: now, log: log, lookups: lookups,
	}, nil
}

func (c *Catalog) GetPublic(ctx context.Context, id uuid.UUID) (domain.Product, error) {
	return cached(ctx, c, "product", "product:"+id.String(),
		func(ctx context.Context) (domain.Product, error) { return c.products.GetPublic(ctx, id) },
		func(ctx context.Context, p domain.Product) time.Duration {
			return c.capTTL(ctx, c.opts.ProductTTL, campaignEnd(p))
		},
	)
}

func (c *Catalog) ListPublic(ctx context.Context, in service.ListProductsInput) (domain.Page[domain.Product], error) {
	return cached(ctx, c, "products", "products:"+hashKey(in),
		func(ctx context.Context) (domain.Page[domain.Product], error) { return c.products.ListPublic(ctx, in) },
		func(ctx context.Context, page domain.Page[domain.Product]) time.Duration {
			ends := make([]time.Time, 0, len(page.Items))
			for _, p := range page.Items {
				ends = append(ends, campaignEnd(p))
			}
			return c.capTTL(ctx, c.opts.ListTTL, ends...)
		},
	)
}

// ListCategoryCounts guarda a faceta de tipos como a de marcas (mesmo TTL, pelo mesmo motivo).
func (c *Catalog) ListCategoryCounts(ctx context.Context, in service.ListProductsInput) ([]domain.CategoryCount, error) {
	return cached(ctx, c, "category_counts", "category-counts:"+hashKey(in),
		func(ctx context.Context) ([]domain.CategoryCount, error) {
			return c.products.ListCategoryCounts(ctx, in)
		},
		func(ctx context.Context, _ []domain.CategoryCount) time.Duration {
			return c.capTTL(ctx, c.opts.ListTTL)
		})
}

// ListBrands guarda a faceta de marcas como as listagens; o TTL vai até a próxima virada de campanha,
// porque a faceta de "só em promoção" e a faixa de preço dependem do desconto vigente.
func (c *Catalog) ListBrands(ctx context.Context, in service.ListProductsInput) ([]domain.BrandCount, error) {
	return cached(ctx, c, "brands", "brands:"+hashKey(in),
		func(ctx context.Context) ([]domain.BrandCount, error) { return c.products.ListBrands(ctx, in) },
		func(ctx context.Context, _ []domain.BrandCount) time.Duration { return c.capTTL(ctx, c.opts.ListTTL) })
}

func (c *Catalog) List(ctx context.Context) ([]domain.Category, error) {
	return cached(ctx, c, "categories", "categories", c.categories.List,
		func(context.Context, []domain.Category) time.Duration { return c.opts.ListTTL })
}

func (c *Catalog) ListActive(ctx context.Context) ([]domain.Campaign, error) {
	return cached(ctx, c, "campaigns", "campaigns", c.campaigns.ListActive,
		func(ctx context.Context, cs []domain.Campaign) time.Duration {
			ends := make([]time.Time, len(cs))
			for i, camp := range cs {
				ends[i] = camp.EndsAt
			}
			return c.capTTL(ctx, c.opts.ListTTL, ends...)
		})
}

// Invalidate descarta tudo que está no cache (nova geração). Chamado depois das escritas admin.
func (c *Catalog) Invalidate(ctx context.Context) {
	if _, err := c.store.Incr(context.WithoutCancel(ctx), generationKey); err != nil {
		// sem a nova geração, leituras podem ficar velhas até o TTL (no máximo ProductTTL)
		c.log.ErrorContext(ctx, "falha ao invalidar o cache", slog.String("error", err.Error()))
	}
}

// cached faz o cache-aside de uma leitura: hit devolve do Redis; miss carrega (uma vez só por
// chave, mesmo com muitas requisições simultâneas) e grava com o TTL de ttlFor.
func cached[T any](
	ctx context.Context, c *Catalog, kind, key string,
	load func(context.Context) (T, error), ttlFor func(context.Context, T) time.Duration,
) (T, error) {
	gen, err := c.generation(ctx)
	if err != nil {
		c.record(ctx, kind, "error", err)
		return load(ctx)
	}
	fullKey := keyPrefix + gen + ":" + key

	raw, err := c.store.Get(ctx, fullKey)
	switch {
	case err == nil:
		var v T
		if jsonErr := json.Unmarshal(raw, &v); jsonErr == nil {
			c.record(ctx, kind, "hit", nil)
			return v, nil
		} else {
			c.record(ctx, kind, "error", jsonErr) // formato antigo/corrompido: recarrega e sobrescreve
		}
	case errors.Is(err, ErrMiss):
		c.record(ctx, kind, "miss", nil)
	default:
		c.record(ctx, kind, "error", err)
		return load(ctx) // Redis fora: não tenta gravar
	}

	v, err, _ := c.group.Do(fullKey, func() (any, error) {
		// o primeiro a chegar carrega para todos: não pode falhar porque ELE desistiu
		loadCtx := context.WithoutCancel(ctx)
		v, err := load(loadCtx)
		if err != nil {
			return v, err
		}
		if ttl := ttlFor(loadCtx, v); ttl > 0 {
			if b, err := json.Marshal(v); err == nil {
				if err := c.store.Set(loadCtx, fullKey, b, ttl); err != nil {
					c.log.WarnContext(ctx, "falha ao gravar no cache", slog.String("key", fullKey), slog.String("error", err.Error()))
				}
			}
		}
		return v, nil
	})
	if err != nil {
		var zero T
		return zero, err //nolint:wrapcheck // é o erro do próprio load (service), já no formato do domínio
	}
	return v.(T), nil //nolint:forcetypeassert // o singleflight devolve o que load devolveu
}

func (c *Catalog) generation(ctx context.Context) (string, error) {
	raw, err := c.store.Get(ctx, generationKey)
	if errors.Is(err, ErrMiss) {
		return "0", nil
	}
	if err != nil {
		return "", err
	}
	n, err := strconv.ParseInt(string(raw), 10, 64)
	if err != nil {
		return "", fmt.Errorf("geração do cache inválida %q: %w", raw, err)
	}
	return strconv.FormatInt(n, 10), nil
}

func (c *Catalog) record(ctx context.Context, kind, result string, err error) {
	c.lookups.Add(ctx, 1, metric.WithAttributes(attribute.String("kind", kind), attribute.String("result", result)))
	setHeader(ctx, result)
	if err != nil {
		c.log.WarnContext(ctx, "cache indisponível, lendo do banco", slog.String("kind", kind), slog.String("error", err.Error()))
	}
}

// capTTL encurta o TTL para a entrada expirar quando o preço efetivo pode mudar: no fim da primeira
// campanha envolvida (ends) ou no início da próxima campanha, de qualquer produto. O início vale
// para todos porque a entrada guardada antes dele não sabe que o produto vai entrar em promoção.
// Instantes zero são ignorados.
func (c *Catalog) capTTL(ctx context.Context, ttl time.Duration, ends ...time.Time) time.Duration {
	next, err := c.campaigns.NextStart(ctx)
	if err != nil {
		// sem saber quando a próxima campanha começa, não guarda: evita servir preço sem desconto
		c.log.WarnContext(ctx, "próxima campanha desconhecida, resposta fora do cache", slog.String("error", err.Error()))
		return 0
	}
	now := c.now()
	if !next.IsZero() {
		ttl = min(ttl, next.Sub(now))
	}
	for _, end := range ends {
		if !end.IsZero() {
			ttl = min(ttl, end.Sub(now))
		}
	}
	return ttl
}

func campaignEnd(p domain.Product) time.Time {
	if p.ActiveCampaign == nil {
		return time.Time{}
	}
	return p.ActiveCampaign.EndsAt
}

// hashKey resume os parâmetros da listagem numa chave curta e estável.
func hashKey(v any) string {
	b, _ := json.Marshal(v) //nolint:errchkjson // structs simples, sem canais nem funções
	sum := sha256.Sum256(b)
	return hex.EncodeToString(sum[:12])
}
