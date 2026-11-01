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
    "/v1/me/invoices/{order_id}/return-danfe": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * O DANFE da nota de devolução do pedido, em PDF
         * @description A NF-e de devolução (#223), quando o cliente desiste da compra. Só o dono do pedido (o `sub` do token) ou
         *     a equipe (role `admin`); a nota de outro cliente, e a que ainda não saiu, dão 404.
         */
        get: operations["getReturnInvoiceDanfe"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/shipping/quote": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Frete e prazo por CEP
         * @description As opções de envio para o CEP (#222, Decreto 7.962: o cliente vê frete e prazo antes de pagar), da
         *     mais barata para a mais cara. O fornecedor cota em dólar; a loja converte pelo câmbio do catálogo
         *     (`Shipping:UsdToBrl`) e soma o processamento dela (`Shipping:ProcessingDays`) ao prazo, em dias
         *     úteis. Pública: a página do produto e o carrinho cotam sem login. O orders confere por aqui o frete
         *     do pedido.
         */
        post: operations["quoteShipping"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier/status": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Como está a integração com o fornecedor
         * @description Pergunta ao fornecedor agora, com até 3 s de espera, e conta o que a loja sabe dele. As credenciais ficam
         *     nos segredos do ambiente e nunca saem daqui: `own_credentials` só diz se não são as do simulador.
         */
        get: operations["getSupplierStatus"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/order-margins": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * A margem real dos pedidos mais recentes
         * @description O que o cliente pagou, menos o custo dos produtos no câmbio da compra, o frete repassado ao fornecedor e a
         *     taxa do pagamento. Em centavos de real. A margem é nula no pedido estornado, e se faltar um dos valores
         *     (produto que não veio do fornecedor, ou pedido de antes desta conta).
         */
        get: operations["listOrderMargins"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-orders/failed": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Pedidos que não chegaram ao fornecedor
         * @description Os recusados, cancelados ou sem resposta do fornecedor, e os de nota rejeitada, dos últimos 30 dias, os
         *     mais recentes primeiro. O cliente já foi estornado: não há reenvio, porque o pedido sairia sem pagamento.
         */
        get: operations["listFailedSupplierOrders"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-orders/{order_id}/retry": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Tenta agora o pedido que espera a próxima tentativa
         * @description Só o pedido na fila depois de um erro passageiro; o resto dá 409.
         */
        post: operations["retrySupplierOrder"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-orders/{order_id}/cancel": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Cancela o pedido que não foi despachado e estorna o cliente
         * @description Cancela no fornecedor, se ele já tem o pedido, e publica `SupplierOrderFailed` com `store_cancelled`: o
         *     orders cancela e o payments estorna. Despachado ou sendo enviado agora dá 409. Fornecedor fora do ar dá
         *     503, e nada muda. Repetir é seguro.
         */
        post: operations["cancelSupplierOrder"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/supplier-returns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Devoluções ao fornecedor que pedem atenção
         * @description As devoluções que o cliente pediu ao desistir da compra (#223) e o fornecedor ainda não resolveu, as
         *     paradas além do prazo (`Supplier:Deadlines:ReturnWithin`, padrão 15 dias, contado do pedido) primeiro.
         *     As que o fornecedor recusou ficam por 30 dias. A nota de devolução rejeitada vem em `invoice_error`.
         */
        get: operations["listSupplierReturns"];
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
        ShippingQuoteRequest: {
            /**
             * @description O CEP, com ou sem hífen.
             * @example 01310-100
             */
            zip_code: string;
            items: {
                /** @description O SKU da loja (o vid no fornecedor). */
                sku: string;
                quantity: number;
            }[];
        };
        ShippingQuote: {
            zip_code: string;
            options: components["schemas"]["ShippingOption"][];
        };
        ShippingOption: {
            /** @description O serviço (ex.: `economico`, `expresso`), o que o pedido escolhe. */
            id: string;
            /** @description O nome que o cliente vê. */
            name: string;
            /** Format: int64 */
            price_cents: number;
            /** @description Dias úteis, já com o processamento da loja. */
            min_days: number;
            max_days: number;
        };
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
        SupplierStatus: {
            name: string;
            /** @description O endereço da API do fornecedor. */
            host: string;
            reachable: boolean;
            /** Format: int64 */
            latency_ms?: number | null;
            error?: string | null;
            /** @description Falso enquanto usa as credenciais do simulador. */
            own_credentials: boolean;
            webhook_secret_configured: boolean;
            products_in_feed: number;
            /** Format: date-time */
            last_stock_read_at?: string | null;
            /** Format: date-time */
            last_webhook_at?: string | null;
            /** @description O prazo de despacho (`Supplier:Deadlines:ShipWithin`). */
            ship_within_hours: number;
            /** @description O prazo de transporte (`Supplier:Deadlines:DeliverWithin`). */
            deliver_within_hours: number;
            /** @description O prazo de resposta a uma devolução (`Supplier:Deadlines:ReturnWithin`). */
            return_within_hours: number;
        };
        /** @description A conta de um pedido, em centavos de real. */
        OrderMargin: {
            /** Format: uuid */
            order_id: string;
            /** @enum {string} */
            outcome: "in_progress" | "delivered" | "cancelled" | "returned";
            /**
             * Format: int64
             * @description O que o cliente pagou, com o frete.
             */
            revenue_cents?: number | null;
            /**
             * Format: int64
             * @description O custo dos produtos, no câmbio da compra. Nulo se algum não veio do fornecedor.
             */
            product_cost_cents?: number | null;
            /**
             * Format: int64
             * @description O frete cobrado, repassado ao fornecedor pelo preço que ele cotou.
             */
            freight_cents?: number | null;
            /**
             * Format: int64
             * @description A taxa do provedor de pagamento.
             */
            fee_cents?: number | null;
            /**
             * Format: int64
             * @description Pode ser negativa. Nula se faltar um dos valores, ou se o cliente foi estornado.
             */
            margin_cents?: number | null;
            /** Format: email */
            customer_email: string;
            /** Format: date-time */
            created_at: string;
        };
        /** @description Um pedido que não chegou ao fornecedor. O cliente já foi estornado. */
        FailedSupplierOrder: {
            /** Format: uuid */
            order_id: string;
            /** @enum {string} */
            reason: "out_of_stock" | "rejected" | "supplier_cancelled" | "supplier_unavailable" | "invoice_rejected" | "store_cancelled";
            detail?: string | null;
            supplier: string;
            supplier_order_id?: string | null;
            /** Format: email */
            customer_email: string;
            attempts: number;
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            failed_at: string;
        };
        /** @description A devolução de um pedido ao fornecedor. O cliente já foi estornado; isto é a pendência da loja. */
        SupplierReturn: {
            /** Format: uuid */
            order_id: string;
            /**
             * @description `pending`: na fila para abrir. `open`: esperando a resposta do fornecedor. `rejected`: ele recusou.
             * @enum {string}
             */
            status: "pending" | "open" | "approved" | "rejected";
            /** @description Passou do prazo sem resposta do fornecedor. */
            overdue: boolean;
            /** Format: date-time */
            due_at: string;
            supplier: string;
            supplier_order_id?: string | null;
            dispute_id?: string | null;
            /** @description O código que o cliente escreve no pacote. */
            return_code?: string | null;
            /** Format: email */
            customer_email: string;
            items: {
                sku: string;
                name: string;
                quantity: number;
            }[];
            /** @description Tentativas de abrir com o fornecedor fora do ar. */
            attempts: number;
            last_error?: string | null;
            /** @description Por que o fornecedor recusou. */
            resolution_reason?: string | null;
            /** @description Por que a SEFAZ rejeitou a nota de devolução. */
            invoice_error?: string | null;
            /** Format: date-time */
            requested_at: string;
            /** Format: date-time */
            opened_at?: string | null;
            /** Format: date-time */
            resolved_at?: string | null;
        };
        /**
         * @description Formato do CJ Dropshipping, `messageType` UPDATE: `ORDER` (o andamento de um pedido), `STOCK` (a
         *     disponibilidade de uma variante, #218) ou `DISPUTE` (a resposta a uma devolução, #223).
         */
        SupplierWebhook: {
            messageId: string;
            /** @description `ORDER`, `STOCK` ou `DISPUTE`; outros tipos são ignorados. */
            type: string;
            /** @description `UPDATE`. */
            messageType: string;
            /** @description `ORDER`: `orderNumber` e `orderStatus` obrigatórios. `STOCK`: `vid` e `storageNum` obrigatórios. `DISPUTE`: `orderNumber` e `disputeStatus` obrigatórios. */
            params: {
                /** @description `DISPUTE`: `PROCESSING`, `APPROVED` ou `REJECTED`. */
                disputeStatus?: string;
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
        /** @description Limite de requisições excedido (rate limit do gateway). */
        TooManyRequests: {
            headers: {
                /** @description Segundos até poder tentar de novo. */
                "Retry-After"?: number;
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
    getReturnInvoiceDanfe: {
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
    quoteShipping: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ShippingQuoteRequest"];
            };
        };
        responses: {
            /** @description As opções de envio. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ShippingQuote"];
                };
            };
            422: components["responses"]["UnprocessableEntity"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
            /** @description O fornecedor não cotou; vem com `Retry-After`. */
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
    getSupplierStatus: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description O status. Fornecedor fora do ar também é 200, com `reachable` falso. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["SupplierStatus"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            500: components["responses"]["InternalError"];
        };
    };
    listOrderMargins: {
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
            /** @description Pedidos, dos mais recentes aos mais antigos. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["OrderMargin"][];
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    listFailedSupplierOrders: {
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
            /** @description Pedidos que falharam. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["FailedSupplierOrder"][];
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    retrySupplierOrder: {
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
            /** @description Volta para a fila agora. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            500: components["responses"]["InternalError"];
        };
    };
    cancelSupplierOrder: {
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
            /** @description Cancelado. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            500: components["responses"]["InternalError"];
            /** @description O fornecedor não respondeu, e nada mudou; vem com `Retry-After`. */
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
    listSupplierReturns: {
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
            /** @description Devoluções. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["SupplierReturn"][];
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
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
