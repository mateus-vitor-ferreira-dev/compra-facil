-- Marca do produto (#179): filtro e faceta da listagem. Entra também no texto da busca, para
-- "notebook dell" achar o produto mesmo quando a marca não está no nome.
ALTER TABLE products ADD COLUMN brand varchar(80);

DROP INDEX products_search_trgm_idx;
ALTER TABLE products DROP COLUMN search_text;
ALTER TABLE products ADD COLUMN search_text text GENERATED ALWAYS AS
    (lower(f_unaccent(sku || ' ' || name || ' ' || coalesce(brand, '') || ' ' || description))) STORED;
CREATE INDEX products_search_trgm_idx ON products USING gin (search_text gin_trgm_ops) WHERE deleted_at IS NULL;

-- filtro por marca sem diferenciar maiúsculas, e a contagem da faceta por categoria
CREATE INDEX products_brand_idx ON products (lower(brand), category_id) WHERE deleted_at IS NULL AND brand IS NOT NULL;
