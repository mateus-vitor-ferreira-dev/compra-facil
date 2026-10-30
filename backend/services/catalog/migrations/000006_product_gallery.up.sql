-- As outras fotos do produto (#207), na ordem da galeria. A primeira continua em image_url e image, que a
-- listagem usa. Cada foto: {"url", "image": {"width", "height", "variants"}}.
ALTER TABLE products ADD COLUMN gallery jsonb NOT NULL DEFAULT '[]';
