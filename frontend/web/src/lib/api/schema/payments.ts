// Gerado de contracts/openapi/payments.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/payments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Pagamentos de um pedido
         * @description Normalmente um por pedido; mais de um só se houver nova tentativa após falha.
         */
        get: operations["listPayments"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/payments/{payment_id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Detalhe de um pagamento */
        get: operations["getPayment"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/webhooks/payments": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Notificação do provedor (uma cobrança mudou)
         * @description No formato do Mercado Pago: `?data.id=<id>&type=payment` e os headers `x-request-id` e
         *     `x-signature: ts=<ts>,v1=<hex>`, com `v1 = HMAC-SHA256(segredo, "id:<data.id>;request-id:<x-request-id>;ts:<ts>;")`.
         *     O corpo não é usado: a cobrança é consultada no provedor. Cobrança desconhecida também responde
         *     200, para o provedor não insistir.
         */
        post: operations["receivePaymentNotification"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/payments/{payment_id}/refunds": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Estorna parte ou o resto de um pagamento aprovado
         * @description Devolução parcial (um item devolvido, #223) ou do que falta. Idempotente pelo `Idempotency-Key`:
         *     a mesma chave não estorna duas vezes. Publica `PaymentRefunded`.
         */
        post: operations["refundPayment"];
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
         * @description Mensagens em `payments.dlt` que chegaram depois do último reprocessamento: as que falharam em
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
         * @description Devolve as pendentes para `payments.retry-1`: só o payments as consome de novo. Falhando outra
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
        /**
         * @description `processing`: enviado ao provedor (ou o cartão em análise) · `pending`: PIX esperando o pagamento ·
         *     `authorized`: cartão com o valor garantido, a captura vem quando o fornecedor aceita · `approved`:
         *     pago ou capturado (pode ter estorno parcial, `refunded_cents`) · `failed` · `voided`: autorização
         *     cancelada, nada cobrado · `refunded`: tudo devolvido.
         * @enum {string}
         */
        PaymentStatus: "processing" | "pending" | "authorized" | "approved" | "failed" | "voided" | "refunded";
        /** @enum {string} */
        PaymentMethod: "pix" | "card";
        /**
         * @description `pix_expired`: o PIX não foi pago até a validade.
         * @enum {string}
         */
        FailureReason: "card_declined" | "insufficient_funds" | "fraud_suspected" | "gateway_error" | "pix_expired";
        Pix: {
            /** @description O "copia e cola" (BR Code). */
            qr_code: string;
            /** @description O QR em PNG, em base64. */
            qr_code_base64: string;
            /**
             * Format: uri
             * @description A página de pagamento do provedor.
             */
            ticket_url?: string | null;
            /** Format: date-time */
            expires_at: string;
        };
        Payment: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            order_id: string;
            amount: components["schemas"]["Money"];
            status: components["schemas"]["PaymentStatus"];
            failure_reason?: components["schemas"]["FailureReason"] | null;
            method: components["schemas"]["PaymentMethod"];
            installments: number;
            /** @enum {string} */
            provider: "mercadopago";
            /** @description ID da cobrança no provedor. */
            provider_reference?: string | null;
            card_last_four?: string | null;
            pix?: components["schemas"]["Pix"] | null;
            /**
             * Format: int64
             * @description A taxa do provedor (Payments:Fees), para a margem real do pedido.
             */
            fee_cents: number;
            /** Format: int64 */
            refunded_cents: number;
            /** Format: date-time */
            captured_at?: string | null;
            /** Format: date-time */
            refunded_at?: string | null;
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            updated_at: string;
        };
        /** @description Valor monetário em centavos, para evitar erro de ponto flutuante. */
        Money: {
            /**
             * Format: int64
             * @example 19990
             */
            amount_cents: number;
            /**
             * @description Código ISO 4217.
             * @default BRL
             * @example BRL
             */
            currency: string;
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
        /**
         * @description Chave única por operação (UUID recomendado). Repetir a mesma chave com o mesmo corpo devolve
         *     a resposta original; com corpo diferente, responde 422.
         */
        IdempotencyKey: string;
        /** @description Máximo de mensagens devolvidas. */
        DeadLetterLimit: number;
    };
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    listPayments: {
        parameters: {
            query: {
                order_id: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Pagamentos. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["Payment"][];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    getPayment: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                payment_id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Pagamento. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Payment"];
                };
            };
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    receivePaymentNotification: {
        parameters: {
            query: {
                "data.id": string;
                type?: string;
            };
            header: {
                "x-signature": string;
                "x-request-id"?: string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: {
            content: {
                "application/json": Record<string, never>;
            };
        };
        responses: {
            /** @description Recebida. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        /** @enum {string} */
                        result: "applied" | "ignored" | "unknown_payment";
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            500: components["responses"]["InternalError"];
        };
    };
    refundPayment: {
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
                payment_id: string;
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": {
                    /**
                     * Format: int64
                     * @description Nulo estorna o que falta.
                     */
                    amount_cents?: number | null;
                };
            };
        };
        responses: {
            /** @description O pagamento depois do estorno. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Payment"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
            /** @description O provedor não respondeu; nada foi estornado, tente de novo com a mesma chave. */
            503: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
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
