-- Os tamanhos que o cliente escolhe na compra (ex.: ["P", "M", "G"]), na ordem de exibição. Vazio: o produto não
-- tem tamanho. Preço e estoque continuam do produto.
ALTER TABLE products ADD COLUMN sizes jsonb NOT NULL DEFAULT '[]';
