DROP INDEX products_brand_idx;

DROP INDEX products_search_trgm_idx;
ALTER TABLE products DROP COLUMN search_text;
ALTER TABLE products ADD COLUMN search_text text GENERATED ALWAYS AS
    (lower(f_unaccent(sku || ' ' || name || ' ' || description))) STORED;
CREATE INDEX products_search_trgm_idx ON products USING gin (search_text gin_trgm_ops) WHERE deleted_at IS NULL;

ALTER TABLE products DROP COLUMN brand;
