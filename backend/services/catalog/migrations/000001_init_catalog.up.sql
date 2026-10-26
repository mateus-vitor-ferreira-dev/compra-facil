-- Esquema inicial do catálogo: categorias, produtos e campanhas promocionais.
CREATE EXTENSION IF NOT EXISTS pg_trgm;
CREATE EXTENSION IF NOT EXISTS unaccent;

-- unaccent() é STABLE; colunas geradas e índices exigem IMMUTABLE (dicionário fixo torna seguro)
CREATE FUNCTION f_unaccent(text) RETURNS text
    LANGUAGE sql IMMUTABLE PARALLEL SAFE STRICT
    RETURN public.unaccent('public.unaccent'::regdictionary, $1);

CREATE TABLE categories (
    id         uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    slug       text        NOT NULL UNIQUE,
    name       text        NOT NULL,
    parent_id  uuid        REFERENCES categories (id),
    created_at timestamptz NOT NULL DEFAULT now(),
    updated_at timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT categories_not_own_parent CHECK (parent_id IS DISTINCT FROM id)
);
CREATE INDEX categories_parent_id_idx ON categories (parent_id);

CREATE TABLE products (
    id          uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    sku         text        NOT NULL,
    name        text        NOT NULL,
    description text        NOT NULL DEFAULT '',
    category_id uuid        NOT NULL REFERENCES categories (id),
    image_url   text,
    price_cents bigint      NOT NULL CHECK (price_cents > 0),
    currency    char(3)     NOT NULL DEFAULT 'BRL',
    status      text        NOT NULL CHECK (status IN ('draft', 'active', 'inactive')),
    version     integer     NOT NULL DEFAULT 1,
    created_at  timestamptz NOT NULL DEFAULT now(),
    updated_at  timestamptz NOT NULL DEFAULT now(),
    deleted_at  timestamptz,
    -- texto normalizado (minúsculo, sem acento) usado pela busca trigram
    search_text text GENERATED ALWAYS AS (lower(f_unaccent(sku || ' ' || name || ' ' || description))) STORED
);

-- SKU único entre os produtos não removidos (um SKU removido pode ser recadastrado)
CREATE UNIQUE INDEX products_sku_key ON products (sku) WHERE deleted_at IS NULL;
-- listagem pública: mais novos primeiro, paginação por (created_at, id)
CREATE INDEX products_listing_idx ON products (status, created_at DESC, id DESC) WHERE deleted_at IS NULL;
CREATE INDEX products_category_id_idx ON products (category_id) WHERE deleted_at IS NULL;
-- busca tolerante a erro de digitação: word_similarity (<%) usa este índice
CREATE INDEX products_search_trgm_idx ON products USING gin (search_text gin_trgm_ops) WHERE deleted_at IS NULL;

CREATE TABLE campaigns (
    id               uuid        PRIMARY KEY DEFAULT gen_random_uuid(),
    slug             text        NOT NULL UNIQUE,
    name             text        NOT NULL,
    discount_percent integer     NOT NULL CHECK (discount_percent BETWEEN 1 AND 90),
    starts_at        timestamptz NOT NULL,
    ends_at          timestamptz NOT NULL,
    created_at       timestamptz NOT NULL DEFAULT now(),
    updated_at       timestamptz NOT NULL DEFAULT now(),
    CONSTRAINT campaigns_period_valid CHECK (ends_at > starts_at)
);
CREATE INDEX campaigns_period_idx ON campaigns (starts_at, ends_at);

CREATE TABLE campaign_products (
    campaign_id uuid NOT NULL REFERENCES campaigns (id) ON DELETE CASCADE,
    product_id  uuid NOT NULL REFERENCES products (id),
    PRIMARY KEY (campaign_id, product_id)
);
CREATE INDEX campaign_products_product_id_idx ON campaign_products (product_id);
