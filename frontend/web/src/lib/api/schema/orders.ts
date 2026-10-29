// Gerado de contracts/openapi/orders.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/orders": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Cria um pedido e inicia a SAGA de compra
         * @description Valida os itens no catálogo (existem, estão ativos, preço vigente) e grava o pedido como
         *     `pending` junto com o evento `OrderPlaced` (outbox). Requisições repetidas com o mesmo
         *     `Idempotency-Key` devolvem o pedido já criado.
         *
         *     Com o catálogo fora ou lento (prazo, novas tentativas e circuit breaker), a validação e o
         *     preço saem da cópia local do catálogo que o orders mantém pelos eventos, com as mesmas
         *     regras (campanha vigente de maior desconto).
         *
         *     O cliente e o e-mail dos avisos são os do token (claims `sub` e `email`), não do corpo.
         */
        post: operations["placeOrder"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/me/orders": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Os pedidos do cliente do token ("Meus pedidos")
         * @description Do mais recente para o mais antigo.
         */
        get: operations["listMyOrders"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/orders/{order_id}": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Detalhe e situação de um pedido
         * @description Do dono do pedido (o cliente do token) ou do operador (`admin`). Pedido de outro cliente dá
         *     `404`, como se não existisse: o id não revela que o pedido existe.
         */
        get: operations["getOrder"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/orders/{order_id}/events": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * O pedido ao vivo (Server-Sent Events)
         * @description Stream `text/event-stream` com o estado atual do pedido e cada mudança da SAGA, até o estado final
         *     (`delivered`, ou `cancelled` sem estorno pendente), quando a conexão fecha. Mesmas regras de acesso do `getOrder`.
         *
         *     - Cada evento é `event: order`, com `id` (muda a cada transição) e `data` com o `Order` em JSON.
         *     - Linhas `: keep-alive` a cada 15 s, sem dado: mantêm a conexão aberta no gateway e em proxies.
         *     - O stream fecha depois de 5 min; reconecte mandando o último `id` em `Last-Event-ID` (o
         *       `EventSource` do navegador faz isso sozinho, com o `retry: 3000` que o stream informa). Com
         *       `Last-Event-ID` igual ao estado atual, ele não é reenviado.
         *     - Pedido já no estado final e `Last-Event-ID` igual a ele: `204`, que faz o `EventSource` parar.
         *
         *     No navegador, o `EventSource` não manda `Authorization`: o front assina pelo BFF, que repassa o
         *     stream com o token.
         */
        get: operations["streamOrderEvents"];
        put?: never;
        post?: never;
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
         * @description Mensagens em `orders.dlt` que chegaram depois do último reprocessamento: as que falharam em
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
         * @description Devolve as pendentes para `orders.retry-1`: só o orders as consome de novo. Falhando outra
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
         * @description `pending`: reservando o estoque e cobrando · `confirmed`: pago, com o fornecedor ·
         *     `shipped`: despachado, com o rastreio · `delivered`: entregue · `cancelled`: não concluído
         *     (ver `cancellation_reason`).
         * @enum {string}
         */
        OrderStatus: "pending" | "confirmed" | "shipped" | "delivered" | "cancelled";
        /**
         * @description `out_of_stock`: inventory recusou a reserva · `payment_failed`: payments recusou a cobrança ·
         *     `supplier_failed`: depois de pago, o fornecedor não atendeu, e o cliente é estornado
         *     (`refunded_at`).
         * @enum {string}
         */
        CancellationReason: "out_of_stock" | "payment_failed" | "supplier_failed";
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
        PlaceOrderRequest: {
            payment: components["schemas"]["PlaceOrderPayment"];
            /** @description Um item por produto (sem repetir `product_id`). */
            items: components["schemas"]["PlaceOrderItem"][];
        };
        /**
         * @description Como pagar (#220). PIX: o QR aparece no pedido, para pagar em 30 minutos. Cartão: o
         *     `card_token` do provedor (o cartão é tokenizado no checkout; os dados dele não passam pela loja),
         *     a bandeira e as parcelas.
         */
        PlaceOrderPayment: {
            method: components["schemas"]["PaymentMethod"];
            /** @description Obrigatório no cartão; de uso único. */
            card_token?: string;
            /** @description Bandeira, como o provedor devolveu na tokenização (ex.: `visa`). */
            card_brand?: string;
            /**
             * @description Parcelas no cartão; PIX é à vista.
             * @default 1
             */
            installments: number;
        };
        OrderPayment: {
            method: components["schemas"]["PaymentMethod"];
            installments: number;
            /** @description O PIX a pagar, depois que o payments o gera; nulo no cartão e antes disso. */
            pix: components["schemas"]["Pix"] | null;
        };
        PlaceOrderItem: {
            /** Format: uuid */
            product_id: string;
            quantity: number;
            /**
             * Format: int64
             * @description Preço que o cliente viu. Se informado e diferente do vigente no catálogo, o pedido é
             *     recusado com 409 (evita cobrar um preço que mudou no meio da campanha).
             */
            expected_unit_price_cents?: number;
        };
        Order: {
            /** Format: uuid */
            id: string;
            /** Format: uuid */
            customer_id: string;
            status: components["schemas"]["OrderStatus"];
            cancellation_reason?: components["schemas"]["CancellationReason"] | null;
            items: components["schemas"]["OrderItem"][];
            total: components["schemas"]["Money"];
            /**
             * Format: uuid
             * @description Preenchido quando o payments processa a cobrança.
             */
            payment_id?: string | null;
            payment: components["schemas"]["OrderPayment"];
            /**
             * Format: date-time
             * @description Quando o inventory reservou o estoque. Com o pedido `pending`, nulo é a etapa da reserva e
             *     preenchido é a da cobrança. Nulo também no pedido cancelado por `out_of_stock`.
             */
            stock_reserved_at?: string | null;
            /**
             * Format: date-time
             * @description Quando o fornecedor aceitou o pedido pago (`confirmed`, antes de `shipped`).
             */
            sent_to_supplier_at?: string | null;
            /** @description Rastreio do envio; nulo antes de `shipped`. */
            tracking?: components["schemas"]["Tracking"] | null;
            /** Format: date-time */
            shipped_at?: string | null;
            /** Format: date-time */
            delivered_at?: string | null;
            /**
             * Format: date-time
             * @description Quando o valor voltou ao cliente, no pedido cancelado por `supplier_failed`. Nulo nesse
             *     pedido é o estorno em andamento.
             */
            refunded_at?: string | null;
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            updated_at: string;
        };
        OrderItem: {
            /** Format: uuid */
            product_id: string;
            sku: string;
            /** @description Nome no momento da compra. */
            name: string;
            quantity: number;
            /** @description Preço efetivo (com campanha) congelado na criação do pedido. */
            unit_price: components["schemas"]["Money"];
            line_total: components["schemas"]["Money"];
            /** Format: uuid */
            campaign_id?: string | null;
        };
        /** @enum {string} */
        PaymentMethod: "pix" | "card";
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
        /** @description Paginação por cursor. Sem `next_cursor`, não há mais páginas. */
        PageInfo: {
            /** @description Cursor opaco para a próxima página; envie de volta em `?cursor=`. */
            next_cursor?: string | null;
            has_more: boolean;
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
        /** @description Token válido, mas sem a permissão necessária. */
        Forbidden: {
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
        /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
        RequestId: string;
        /**
         * @description Chave única por operação (UUID recomendado). Repetir a mesma chave com o mesmo corpo devolve
         *     a resposta original; com corpo diferente, responde 422.
         */
        IdempotencyKey: string;
        /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
        Cursor: string;
        /** @description Itens por página. */
        Limit: number;
        /** @description Máximo de mensagens devolvidas. */
        DeadLetterLimit: number;
    };
    requestBodies: never;
    headers: {
        /** @description URL do recurso criado. */
        Location: string;
        /** @description Identificador da requisição (recebido ou gerado). */
        RequestId: string;
    };
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    placeOrder: {
        parameters: {
            query?: never;
            header: {
                /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
                "X-Request-ID"?: components["parameters"]["RequestId"];
                /**
                 * @description Chave única por operação (UUID recomendado). Repetir a mesma chave com o mesmo corpo devolve
                 *     a resposta original; com corpo diferente, responde 422.
                 */
                "Idempotency-Key": components["parameters"]["IdempotencyKey"];
            };
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["PlaceOrderRequest"];
            };
        };
        responses: {
            /** @description Pedido aceito e em processamento. */
            202: {
                headers: {
                    Location: components["headers"]["Location"];
                    "X-Request-ID": components["headers"]["RequestId"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Order"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description O token não é de cliente (papel `customer`). */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            /**
             * @description Itens recusados pelo catálogo (produto inexistente/inativo ou preço alterado desde a
             *     exibição). O `detail` lista os itens.
             */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
            /** @description Catálogo indisponível e sem snapshot local para validar os itens. */
            503: {
                headers: {
                    /** @description Segundos sugeridos até nova tentativa. */
                    "Retry-After"?: number;
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
        };
    };
    listMyOrders: {
        parameters: {
            query?: {
                /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
                cursor?: components["parameters"]["Cursor"];
                /** @description Itens por página. */
                limit?: components["parameters"]["Limit"];
                status?: components["schemas"]["OrderStatus"];
            };
            header?: {
                /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
                "X-Request-ID"?: components["parameters"]["RequestId"];
            };
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Página de pedidos. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["Order"][];
                        page: components["schemas"]["PageInfo"];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    getOrder: {
        parameters: {
            query?: never;
            header?: {
                /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
                "X-Request-ID"?: components["parameters"]["RequestId"];
            };
            path: {
                order_id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Pedido. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Order"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    streamOrderEvents: {
        parameters: {
            query?: never;
            header?: {
                /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
                "X-Request-ID"?: components["parameters"]["RequestId"];
                /** @description O `id` do último evento recebido (reconexão). */
                "Last-Event-ID"?: string;
            };
            path: {
                order_id: string;
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Stream de eventos `order` (o `data` de cada um segue o schema `Order`). */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "text/event-stream": string;
                };
            };
            /** @description O pedido já está no estado final que o cliente recebeu; não há mais o que enviar. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
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
