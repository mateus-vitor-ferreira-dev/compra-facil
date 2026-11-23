-- As cores que o cliente escolhe na compra (ex.: ["Branco", "Preto"]), na ordem de exibição. Vazio: o produto não
-- tem cor à escolha. Preço e estoque continuam do produto.
ALTER TABLE products ADD COLUMN colors jsonb NOT NULL DEFAULT '[]';
