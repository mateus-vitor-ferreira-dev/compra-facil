-- Dimensões e variantes por largura da foto (#147): o front monta o srcset com elas. Nulo quando a
-- foto foi definida só por URL (sem variantes conhecidas). Volta a nulo se a URL mudar.
ALTER TABLE products ADD COLUMN image jsonb;
