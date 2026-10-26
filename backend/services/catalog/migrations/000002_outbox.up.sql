-- Transactional outbox: o evento é gravado na mesma transação da escrita e publicado no Kafka
-- depois, pelo relay (internal/events). Linha publicada é apagada.
CREATE TABLE outbox (
    -- ordem de publicação; escritas no mesmo agregado se serializam pelo lock da linha, então
    -- dentro de um produto/campanha a ordem do id é a ordem dos commits
    id          bigint      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    event_id    uuid        NOT NULL UNIQUE,
    topic       text        NOT NULL,
    message_key text        NOT NULL,
    event_type  text        NOT NULL,
    -- json (não jsonb): publica exatamente o texto gravado
    payload     json        NOT NULL,
    headers     json        NOT NULL DEFAULT '{}',
    created_at  timestamptz NOT NULL DEFAULT now()
);

-- Versão da campanha, para o consumidor descartar evento fora de ordem (ADR 0002).
-- Sobe em toda alteração, inclusive na troca de produtos.
ALTER TABLE campaigns ADD COLUMN version integer NOT NULL DEFAULT 1;
