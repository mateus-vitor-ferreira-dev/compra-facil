// Gerado de contracts/openapi/fulfillment.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/webhooks/supplier": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Atualização de um pedido no fornecedor
         * @description O fornecedor avisa cada mudança de status de um pedido (aceito, enviado com o rastreio, entregue,
         *     cancelado). A assinatura é um HMAC-SHA256 do corpo com o segredo combinado, no cabeçalho
         *     `X-Sim-Signature` (`sha256=<hex>`; o nome do cabeçalho é configurável por fornecedor).
         *
         *     Idempotente por `messageId`: a mesma mensagem de novo responde 200 sem repetir o efeito. Uma
         *     atualização atrasada não faz o pedido voltar (entregue não volta a enviado). Pedido desconhecido
         *     também responde 200, para o fornecedor não insistir; fica no log.
         */
        post: operations["receiveSupplierWebhook"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/internal/supplier-stock/check": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Confere no fornecedor, agora, a disponibilidade das variantes de um pedido
         * @description Usada pelo inventory antes de reservar e cobrar um pedido com produtos de fornecedor (#218). A
         *     leitura também atualiza o espelho (`SupplierStockChanged`). **Interna**: o gateway não a expõe, só
         *     a rede dos serviços chega nela.
         */
        post: operations["checkSupplierStock"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/me/invoices/{order_id}/danfe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * O DANFE da nota do pedido, em PDF
         * @description A nota fiscal do pedido (#221) para o cliente baixar, como o emissor a gera. Só o dono do pedido (o
         *     `sub` do token) ou a equipe (role `admin`); a nota de outro cliente, a pendente e a rejeitada dão
         *     404. A nota cancelada continua baixável (o DANFE dela mostra o cancelamento no emissor de verdade).
         */
        get: operations["getInvoiceDanfe"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-orders/overdue": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Pedidos parados além do prazo do fornecedor
         * @description Os pedidos pagos que o fornecedor não despachou dentro do prazo de separação
         *     (`Supplier:Deadlines:ShipWithin`, padrão 3 dias, contado do pedido pago) e os enviados que não
         *     chegaram dentro do prazo de transporte (`Supplier:Deadlines:DeliverWithin`, padrão 15 dias,
         *     contado do envio). Os mais atrasados primeiro. Para a equipe cobrar o fornecedor e avisar o
         *     cliente: a loja responde pelo prazo que prometeu.
         */
        get: operations["listOverdueSupplierOrders"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-catalog/sync": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Lê o feed do fornecedor agora
         * @description A mesma leitura que o fulfillment faz de tempos em tempos (`Supplier:CatalogSyncInterval`): o que
         *     mudou desde a anterior vira `SupplierProductUpdated` ou `SupplierProductRemoved`, e o catalog
         *     atualiza a loja. Feed vazio ou com erro não tira nada de venda (503).
         */
        post: operations["syncSupplierCatalog"];
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
         * @description Mensagens em `fulfillment.dlt` que chegaram depois do último reprocessamento: as que falharam em
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
         * @description Devolve as pendentes para `fulfillment.retry-1`: só o fulfillment as consome de novo. Falhando outra
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
        CatalogSyncResult: {
            /** @description Produtos novos no feed. */
            created: number;
            /** @description Alterados ou de volta ao feed. */
            updated: number;
            /** @description Saíram do feed. */
            removed: number;
            unchanged: number;
        };
        OverdueSupplierOrder: {
            /**
             * Format: uuid
             * @description O pedido da loja (o mesmo `order_id` do orders).
             */
            order_id: string;
            /**
             * @description `pending`: na fila para o fornecedor (ou esperando nova tentativa) · `placing`: sendo enviado ·
             *     `placed`: aceito e não despachado · `shipped`: despachado e não entregue.
             * @enum {string}
             */
            status: "pending" | "placing" | "placed" | "shipped";
            /**
             * @description `shipping`: o fornecedor não despachou no prazo · `delivery`: o envio não chegou no prazo.
             * @enum {string}
             */
            stage: "shipping" | "delivery";
            /**
             * Format: date-time
             * @description Quando o prazo da etapa venceu.
             */
            due_at: string;
            supplier: string;
            /** @description O pedido no fornecedor, depois de aceito. */
            supplier_order_id?: string | null;
            /** Format: email */
            customer_email: string;
            /** @description Tentativas de envio ao fornecedor que falharam (fornecedor fora, timeout). */
            attempts: number;
            /** @description O erro da última tentativa. */
            last_error?: string | null;
            /** @description Rastreio, no `shipped`. */
            tracking?: components["schemas"]["Tracking"] | null;
            /**
             * Format: date-time
             * @description Quando o pedido pago chegou ao fulfillment.
             */
            created_at: string;
            /** Format: date-time */
            placed_at?: string | null;
            /** Format: date-time */
            shipped_at?: string | null;
        };
        /**
         * @description Formato do CJ Dropshipping, `messageType` UPDATE: `ORDER` (o andamento de um pedido) ou `STOCK` (a
         *     disponibilidade de uma variante, #218).
         */
        SupplierWebhook: {
            messageId: string;
            /** @description `ORDER` ou `STOCK`; outros tipos são ignorados. */
            type: string;
            /** @description `UPDATE`. */
            messageType: string;
            /** @description `ORDER`: `orderNumber` e `orderStatus` obrigatórios. `STOCK`: `vid` e `storageNum` obrigatórios. */
            params: {
                /** @description `STOCK`: a variante. */
                vid?: string;
                /** @description `STOCK`: unidades disponíveis agora. */
                storageNum?: number;
                /** @description Id do pedido no fornecedor. */
                orderId?: string | null;
                /**
                 * Format: uuid
                 * @description Número do pedido na loja (order_id).
                 */
                orderNumber?: string;
                /** @description `CREATED`, `UNSHIPPED`, `SHIPPED`, `DELIVERED` ou `CANCELLED`. */
                orderStatus?: string;
                subStatus?: string | null;
                trackNumber?: string | null;
                trackingProvider?: string | null;
                trackingUrl?: string | null;
                reason?: string | null;
                /** Format: date-time */
                updateDate?: string | null;
            };
        };
        SupplierWebhookAck: {
            /**
             * @description `applied`: o pedido mudou · `duplicate`: mensagem já recebida · `ignored`: nada a mudar
             *     (atrasada ou repetida em outra mensagem) · `unknown_order`: não é pedido desta loja.
             * @enum {string}
             */
            result: "applied" | "duplicate" | "ignored" | "unknown_order";
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
        Tracking: {
            /** @description Código de rastreio. */
            number: string;
            /** @description Transportadora. */
            provider?: string | null;
            /**
             * Format: uri
             * @description Onde acompanhar a entrega.
             */
            url?: string | null;
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
        /** @description Token ausente, inválido ou expirado. */
        Unauthorized: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
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
        /** @description Corpo bem formado, mas com campos inválidos. */
        UnprocessableEntity: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["ValidationProblem"];
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
        /** @description Token válido, mas sem a permissão necessária. */
        Forbidden: {
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
        /** @description Máximo de mensagens devolvidas. */
        DeadLetterLimit: number;
    };
    requestBodies: never;
    headers: never;
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    receiveSupplierWebhook: {
        parameters: {
            query?: never;
            header: {
                "X-Sim-Signature": string;
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["SupplierWebhook"];
            };
        };
        responses: {
            /** @description Recebida (aplicada, repetida, ignorada ou de pedido desconhecido). */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SupplierWebhookAck"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            500: components["responses"]["InternalError"];
        };
    };
    checkSupplierStock: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": {
                    items: {
                        vid: string;
                        quantity: number;
                    }[];
                };
            };
        };
        responses: {
            /** @description A disponibilidade de cada variante. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        items: {
                            vid: string;
                            available: number;
                            /** @description Falso se a variante não está no feed do fornecedor. */
                            known: boolean;
                        }[];
                    };
                };
            };
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
            /** @description O fornecedor não respondeu; o inventory decide pelo espelho. */
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
    getInvoiceDanfe: {
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
            /** @description O DANFE. */
            200: {
                headers: {
                    /** @description `attachment; filename=nfe-<número>.pdf`. */
                    "Content-Disposition"?: string;
                    [name: string]: unknown;
                };
                content: {
                    "application/pdf": string;
                };
            };
            401: components["responses"]["Unauthorized"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
            /** @description O emissor da nota não respondeu; vem com `Retry-After`. */
            503: {
                headers: {
                    "Retry-After"?: number;
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
        };
    };
    listOverdueSupplierOrders: {
        parameters: {
            query?: {
                limit?: number;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Pedidos atrasados. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["OverdueSupplierOrder"][];
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    syncSupplierCatalog: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description O que a leitura encontrou. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["CatalogSyncResult"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            /** @description Outra leitura do feed está rodando (o job ou outro operador). */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            500: components["responses"]["InternalError"];
            /** @description O fornecedor não respondeu, ou devolveu o feed vazio; nada mudou. */
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
