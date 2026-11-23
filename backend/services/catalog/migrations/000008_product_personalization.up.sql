-- O texto que o cliente escreve na compra, impresso na arte (ex.: o nome na camiseta): o rótulo do campo, o tamanho
-- máximo, se é obrigatório e o id da arte. Nulo: o produto não tem personalização.
ALTER TABLE products ADD COLUMN personalization jsonb;
