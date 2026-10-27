// Gerado de contracts/openapi/inventory.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/stock": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Saldo de vários produtos */
        get: operations["listStock"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/stock/{product_id}": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        /** Saldo de um produto */
        get: operations["getStock"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/reservations/{order_id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Reserva de um pedido
         * @description Útil para depurar a SAGA.
         */
        get: operations["getReservation"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/stock/{product_id}/adjustments": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Ajusta o estoque físico
         * @description Soma `delta` ao `on_hand` (negativo para baixa). Recusa com 409 se o resultado ficar abaixo do
         *     que já está reservado. Cria o registro do produto se ainda não existir.
         */
        post: operations["adjustStock"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/dead-letters": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Mensagens pendentes na dead letter topic
         * @description Mensagens em `inventory.dlt` que chegaram depois do último reprocessamento: as que falharam em
         *     todas as tentativas (no lugar e nos tópicos de retry) e as envenenadas (fora do contrato),
         *     que vão direto para lá. Ver docs/adr/0003-retries-e-dead-letter-topics.md.
         */
        get: operations["listDeadLetters"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/dead-letters/replay": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Reprocessa as mensagens pendentes na dead letter topic
         * @description Devolve as pendentes para `inventory.retry-1`: só o inventory as consome de novo. Falhando outra
         *     vez, voltam para a DLT. Evento já processado não repete o efeito (inbox). Use depois de
         *     corrigir a causa.
         */
        post: operations["replayDeadLetters"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
}
export type webhooks = Record<string, never>;
export interface components {
    schemas: {
        StockLevel: {
            /** Format: uuid */
            product_id: string;
            /** @description Unidades fisicamente em estoque. */
            on_hand: number;
            /** @description Unidades presas a pedidos `pending`. */
            reserved: number;
            /** @description `on_hand - reserved`: o que ainda pode ser vendido. */
            available: number;
            /** Format: date-time */
            updated_at: string;
        };
        /**
         * @description `reserved`: aguardando pagamento · `committed`: pedido confirmado · `released`: devolvida.
         * @enum {string}
         */
        ReservationStatus: "reserved" | "committed" | "released";
        Reservation: {
            /** Format: uuid */
            order_id: string;
            status: components["schemas"]["ReservationStatus"];
            items: {
                /** Format: uuid */
                product_id: string;
                quantity: number;
            }[];
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            updated_at: string;
        };
        StockAdjustment: {
            /** @description Diferente de zero. */
            delta: number;
            /** @enum {string} */
            reason: "restock" | "inventory_count" | "damage" | "other";
            note?: string;
        };
        /** @description Erro no formato ProblemDetails (RFC 9457), servido como `application/problem+json`. */
        Problem: {
            /**
             * Format: uri-reference
             * @description URI que identifica o tipo do problema. `about:blank` quando não há tipo específico.
             * @example about:blank
             */
            type: string;
            /**
             * @description Resumo legível do problema; não muda entre ocorrências do mesmo tipo.
             * @example Recurso não encontrado
             */
            title: string;
            /** @example 404 */
            status: number;
            /** @description Explicação específica desta ocorrência. */
            detail?: string;
            /**
             * Format: uri-reference
             * @description Caminho da requisição que gerou o problema.
             * @example /v1/products/0f8fad5b-d9cb-469f-a165-70867728950e
             */
            instance?: string;
            /** @description Mesmo valor do header `X-Request-ID`, para correlacionar com os logs. */
            request_id?: string;
            /** @description Mesmo valor do header `X-Trace-Id`; abre o trace da requisição no Grafana (Tempo). */
            trace_id?: string;
        };
        /** @description Problema de validação (HTTP 422), com um item por campo inválido. */
        ValidationProblem: components["schemas"]["Problem"] & {
            errors: {
                /**
                 * @description Caminho do campo no corpo (`items[0].quantity`) ou nome do parâmetro.
                 * @example items[0].quantity
                 */
                field: string;
                /** @example deve ser maior que 0 */
                message: string;
            }[];
        };
        DeadLetter: {
            /**
             * Format: uuid
             * @description Nulo quando a mensagem nem é um envelope válido.
             */
            event_id: string | null;
            event_type: string | null;
            /** @description Tópico de origem (ex.: orders.order.v1). */
            topic: string;
            /** @description Partição no tópico de origem. */
            partition: number | null;
            /**
             * Format: int64
             * @description Offset no tópico de origem.
             */
            offset: number | null;
            /** @description Estágios em que a mensagem falhou (origem e cada nível de retry). */
            attempts: number;
            /** @description Última falha (tipo e mensagem da exceção). */
            error: string | null;
            /** Format: date-time */
            failed_at: string | null;
        };
        DeadLetterList: {
            items: components["schemas"]["DeadLetter"][];
        };
        DeadLetterReplay: {
            replayed: number;
        };
    };
    responses: {
        /** @description Requisição malformada (JSON inválido, parâmetro com tipo errado). */
        BadRequest: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Corpo bem formado, mas com campos inválidos. */
        UnprocessableEntity: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["ValidationProblem"];
            };
        };
        /** @description Erro inesperado. */
        InternalError: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Recurso não encontrado. */
        NotFound: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Token ausente, inválido ou expirado. */
        Unauthorized: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Token válido, mas sem a permissão necessária. */
        Forbidden: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Conflito com o estado atual (duplicidade ou versão desatualizada). */
        Conflict: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Kafka fora do ar; tente de novo. */
        KafkaUnavailable: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
    };
    parameters: {
        ProductId: string;
        /**
         * @description Chave única por operação (UUID recomendado). Repetir a mesma chave com o mesmo corpo devolve
         *     a resposta original; com corpo diferente, responde 422.
         */
        IdempotencyKey: string;
        /** @description Máximo de mensagens devolvidas. */
        DeadLetterLimit: number;
    };
    requestBodies: never;
    headers: {
        /** @description Versão do recurso; envie em `If-Match` para atualizar. */
        ETag: string;
    };
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    listStock: {
        parameters: {
            query: {
                /** @description IDs separados por vírgula (até 100). */
                product_ids: string[];
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Saldos. Produtos sem registro de estoque aparecem com tudo zerado. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["StockLevel"][];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    getStock: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Saldo. */
            200: {
                headers: {
                    ETag: components["headers"]["ETag"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StockLevel"];
                };
            };
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    getReservation: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                order_id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Reserva. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Reservation"];
                };
            };
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    adjustStock: {
        parameters: {
            query?: never;
            header: {
                /**
                 * @description Chave única por operação (UUID recomendado). Repetir a mesma chave com o mesmo corpo devolve
                 *     a resposta original; com corpo diferente, responde 422.
                 */
                "Idempotency-Key": components["parameters"]["IdempotencyKey"];
            };
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["StockAdjustment"];
            };
        };
        responses: {
            /** @description Saldo após o ajuste. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["StockLevel"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    listDeadLetters: {
        parameters: {
            query?: {
                /** @description Máximo de mensagens devolvidas. */
                limit?: components["parameters"]["DeadLetterLimit"];
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Mensagens pendentes. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeadLetterList"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            500: components["responses"]["InternalError"];
            503: components["responses"]["KafkaUnavailable"];
        };
    };
    replayDeadLetters: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Quantas mensagens foram devolvidas. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["DeadLetterReplay"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            500: components["responses"]["InternalError"];
            503: components["responses"]["KafkaUnavailable"];
        };
    };
}
