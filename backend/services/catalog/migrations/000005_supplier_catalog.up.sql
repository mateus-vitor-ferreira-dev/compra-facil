-- Dropshipping (#217, ADR 0007): o produto que vem do feed de um fornecedor guarda de onde vem e quanto
-- custa; o preço de venda sai do custo pela regra de margem da categoria.
ALTER TABLE products
    ADD COLUMN supplier varchar(32),
    ADD COLUMN supplier_vid varchar(64),
    ADD COLUMN cost_cents bigint CHECK (cost_cents > 0),
    ADD COLUMN cost_currency char(3),
    -- updated_at do último evento do fornecedor aplicado: um evento mais velho não desfaz um mais novo
    ADD COLUMN supplier_synced_at timestamptz,
    -- saiu do feed (o fornecedor deixou de vender); volta a nulo se ele voltar
    ADD COLUMN supplier_removed_at timestamptz,
    ADD CONSTRAINT products_supplier_complete CHECK (
        (supplier IS NULL AND supplier_vid IS NULL AND cost_cents IS NULL AND cost_currency IS NULL AND supplier_synced_at IS NULL)
        OR (supplier IS NOT NULL AND supplier_vid IS NOT NULL AND cost_cents IS NOT NULL AND cost_currency IS NOT NULL AND supplier_synced_at IS NOT NULL));

-- um produto por variante do fornecedor, inclusive os removidos pela equipe (não voltam pelo feed)
CREATE UNIQUE INDEX products_supplier_vid_idx ON products (supplier, supplier_vid) WHERE supplier IS NOT NULL;

-- margem por categoria; category_id nulo é a regra padrão (uma só)
CREATE TABLE pricing_rules (
    category_id     uuid REFERENCES categories (id) ON DELETE CASCADE,
    margin_percent  integer NOT NULL CHECK (margin_percent BETWEEN 0 AND 1000),
    min_price_cents bigint NOT NULL CHECK (min_price_cents >= 0),
    updated_at      timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX pricing_rules_category_idx ON pricing_rules (category_id) NULLS NOT DISTINCT;

-- eventos de outros serviços já processados (o efeito não se repete na reentrega)
CREATE TABLE inbox_messages (
    event_id     uuid PRIMARY KEY,
    topic        varchar(200) NOT NULL,
    event_type   varchar(100) NOT NULL,
    processed_at timestamptz NOT NULL
);
