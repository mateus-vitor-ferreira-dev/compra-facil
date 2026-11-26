-- O código do produto no fornecedor manual (hoje a Montink) para cada cor à venda (#483), ex.:
-- {"Branco": "6167609", "Preto": "6167609", "Vermelho": "6170123"}; a chave é "" no produto sem cor à escolha. Vazio: o
-- produto ainda não tem fornecedor e não pode ficar à venda (a não ser que venha do feed de dropshipping).
ALTER TABLE products ADD COLUMN supplier_products jsonb NOT NULL DEFAULT '{}';

-- quantos produtos ativos cada categoria tem (a categoria sem nenhum some da loja)
CREATE INDEX products_active_category_idx ON products (category_id) WHERE deleted_at IS NULL AND status = 'active';
