// Gerado de contracts/openapi/catalog.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/products": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Lista e busca produtos
         * @description Com `q`, faz busca por texto (nome, descrição, marca e SKU, tolerante a erro de digitação).
         *     Retorna só produtos `active`. A ordem vem de `sort`; sem ele, relevância com `q` e mais novos sem
         *     `q`. O cursor vale só para a mesma ordem e os mesmos filtros.
         */
        get: operations["listProducts"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/brands": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Marcas dos produtos de uma listagem, com a contagem
         * @description As marcas dos produtos `active` que passam nos mesmos filtros da listagem (sem o próprio
         *     `brand`), da mais frequente para a menos (até 50). O front monta o filtro de marca com elas.
         */
        get: operations["listBrands"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/products/{product_id}": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        /**
         * Detalhe de um produto
         * @description Retorna 404 para produtos inexistentes, removidos ou não ativos.
         */
        get: operations["getProduct"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/categories": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Lista as categorias
         * @description Lista plana; monte a árvore por `parent_id`.
         */
        get: operations["listCategories"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/campaigns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Campanhas vigentes
         * @description Campanhas com `starts_at <= agora < ends_at`.
         */
        get: operations["listActiveCampaigns"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/products": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lista produtos em qualquer status */
        get: operations["adminListProducts"];
        put?: never;
        /** Cria um produto */
        post: operations["createProduct"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/products/{product_id}": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        /** Detalhe de um produto em qualquer status */
        get: operations["adminGetProduct"];
        /**
         * Substitui os dados de um produto
         * @description Publica `catalog.product.v1` / `updated`, e também `price_changed` se o preço mudou.
         *     Exige `If-Match` com o ETag da última leitura.
         */
        put: operations["updateProduct"];
        post?: never;
        /**
         * Remove um produto (soft delete)
         * @description Publica `catalog.product.v1` / `deleted`. Pedidos antigos continuam válidos.
         */
        delete: operations["deleteProduct"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/categories": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /** Cria uma categoria */
        post: operations["createCategory"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/campaigns": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** Lista todas as campanhas (passadas, vigentes e futuras) */
        get: operations["adminListCampaigns"];
        put?: never;
        /** Cria uma campanha */
        post: operations["createCampaign"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/campaigns/{campaign_id}": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        /** Detalhe de uma campanha */
        get: operations["adminGetCampaign"];
        /**
         * Altera uma campanha
         * @description Mudar datas ou desconto publica `price_changed` para os produtos afetados.
         */
        put: operations["updateCampaign"];
        post?: never;
        /** Remove uma campanha */
        delete: operations["deleteCampaign"];
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/admin/campaigns/{campaign_id}/products": {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        /**
         * Os produtos da campanha
         * @description Em qualquer status, por nome (até 1000, o máximo de uma campanha).
         */
        get: operations["adminListCampaignProducts"];
        /**
         * Define os produtos da campanha
         * @description Substitui a lista inteira. Um produto só pode estar em uma campanha por período.
         */
        put: operations["setCampaignProducts"];
        post?: never;
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
         * @description `draft`: em cadastro · `active`: à venda · `inactive`: fora de venda.
         * @enum {string}
         */
        ProductStatus: "draft" | "active" | "inactive";
        /**
         * @description Código do produto, único. Letras maiúsculas, dígitos e hífen.
         * @example TV-SAMS-55Q
         */
        Sku: string;
        /** @description Produto como o cliente vê. */
        Product: {
            /** Format: uuid */
            id: string;
            sku: components["schemas"]["Sku"];
            /** @example Smart TV 55" QLED */
            name: string;
            description?: string;
            /** @example Samsung */
            brand?: string | null;
            category: components["schemas"]["CategoryRef"];
            /**
             * Format: uri
             * @description A foto (a maior variante, quando há variantes). Para exibir, prefira `image`.
             */
            image_url?: string | null;
            image?: components["schemas"]["ProductImage"] | null;
            /** @description Preço de tabela. */
            price: components["schemas"]["Money"];
            /** @description Preço cobrado agora (igual a `price` quando não há campanha ativa). */
            effective_price: components["schemas"]["Money"];
            /** @description Campanha que gerou o desconto, se houver. */
            campaign?: components["schemas"]["CampaignRef"] | null;
            /** Format: date-time */
            created_at: string;
            /** Format: date-time */
            updated_at: string;
        };
        /**
         * @description A foto do produto com as larguras disponíveis, para o `srcset`. As fotos do seed são quadradas, com
         *     variantes WebP de 320 a 1200 px (nunca maiores que a original), servidas pelo gateway em `/media`
         *     (em produção, pela CDN) com cache permanente: o nome muda quando a foto muda.
         *
         *     No front: `<img srcset>` com as variantes (`url` e `width` de cada uma) e `sizes` de acordo com o
         *     layout; o navegador baixa a menor que serve. O `loader` do `next/image` não serve aqui, porque não
         *     recebe a lista de variantes de cada foto. Foto definida pelo admin só por URL vem sem dimensões e com
         *     `variants` vazio: exiba a `url` em proporção 1:1 com `object-fit: cover`.
         */
        ProductImage: {
            /** Format: uri */
            url: string;
            /** @example 1200 */
            width: number | null;
            /** @example 1200 */
            height: number | null;
            /** @description Da menor para a maior. */
            variants: {
                /** @example 640 */
                width: number;
                /**
                 * Format: uri
                 * @example http://localhost/media/produtos/tv-1.3fa9c2d1-640.webp
                 */
                url: string;
            }[];
        };
        /** @description Produto com os campos de gestão. */
        AdminProduct: components["schemas"]["Product"] & {
            status: components["schemas"]["ProductStatus"];
            /** @description Incrementa a cada alteração; é o valor do ETag. */
            version: number;
        };
        ProductInput: {
            sku: components["schemas"]["Sku"];
            name: string;
            description?: string;
            brand?: string | null;
            /** Format: uuid */
            category_id: string;
            /** Format: uri */
            image_url?: string | null;
            /** Format: int64 */
            price_cents: number;
            status: components["schemas"]["ProductStatus"];
        };
        /** @enum {string} */
        ProductSort: "relevance" | "price_asc" | "price_desc" | "discount" | "newest";
        BrandCount: {
            name: string;
            count: number;
        };
        ProductPage: {
            data: components["schemas"]["Product"][];
            page: components["schemas"]["PageInfo"];
            /** @description Quantos produtos a listagem tem, com os filtros; só com `page`. */
            total?: number;
        };
        Category: {
            /** Format: uuid */
            id: string;
            /** @example televisores */
            slug: string;
            /** @example Televisores */
            name: string;
            /** Format: uuid */
            parent_id?: string | null;
        };
        CategoryRef: {
            /** Format: uuid */
            id: string;
            slug: string;
            name: string;
        };
        CategoryInput: {
            slug: string;
            name: string;
            /** Format: uuid */
            parent_id?: string | null;
        };
        Campaign: {
            /** Format: uuid */
            id: string;
            /** @example black-friday-2026 */
            slug: string;
            /** @example Black Friday 2026 */
            name: string;
            /** @example 30 */
            discount_percent: number;
            /** Format: date-time */
            starts_at: string;
            /** Format: date-time */
            ends_at: string;
            product_count?: number;
        };
        CampaignRef: {
            /** Format: uuid */
            id: string;
            name: string;
            discount_percent: number;
            /** Format: date-time */
            ends_at: string;
        };
        /** @description `ends_at` deve ser posterior a `starts_at`. */
        CampaignInput: {
            slug: string;
            name: string;
            discount_percent: number;
            /** Format: date-time */
            starts_at: string;
            /** Format: date-time */
            ends_at: string;
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
        /** @description Paginação por cursor. Sem `next_cursor`, não há mais páginas. */
        PageInfo: {
            /** @description Cursor opaco para a próxima página; envie de volta em `?cursor=`. */
            next_cursor?: string | null;
            has_more: boolean;
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
        /** @description O `If-Match` não corresponde à versão atual do recurso. */
        PreconditionFailed: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
        /** @description Falta o `If-Match`; a atualização exige a versão da última leitura. */
        PreconditionRequired: {
            headers: {
                [name: string]: unknown;
            };
            content: {
                "application/problem+json": components["schemas"]["Problem"];
            };
        };
    };
    parameters: {
        /** @description Uma ou mais marcas, separadas por vírgula (sem diferenciar maiúsculas). */
        Brand: string[];
        ProductId: string;
        CampaignId: string;
        /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
        RequestId: string;
        /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
        Cursor: string;
        /** @description Itens por página. */
        Limit: number;
        /** @description ETag da última leitura do recurso (concorrência otimista). */
        IfMatch: string;
    };
    requestBodies: never;
    headers: {
        /** @description Identificador da requisição (recebido ou gerado). */
        RequestId: string;
        /** @description Versão do recurso; envie em `If-Match` para atualizar. */
        ETag: string;
        /** @description URL do recurso criado. */
        Location: string;
    };
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    listProducts: {
        parameters: {
            query?: {
                /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
                cursor?: components["parameters"]["Cursor"];
                /** @description Itens por página. */
                limit?: components["parameters"]["Limit"];
                /** @description Texto de busca. */
                q?: string;
                /** @description Slug da categoria; inclui as subcategorias. */
                category?: string;
                /** @description Filtra pelo preço efetivo (já com desconto), em centavos. */
                min_price_cents?: number;
                max_price_cents?: number;
                /** @description Só produtos em campanha ativa. */
                on_sale?: boolean;
                /** @description Uma ou mais marcas, separadas por vírgula (sem diferenciar maiúsculas). */
                brand?: components["parameters"]["Brand"];
                /**
                 * @description Paginação por número (a partir de 1), com o `total` da listagem na resposta: a vitrine mostra
                 *     os números das páginas. Alternativa ao `cursor` (os dois juntos dão 422). Até a página 200.
                 */
                page?: number;
                /**
                 * @description `relevance` (só com `q`), `price_asc` e `price_desc` (preço efetivo, já com desconto),
                 *     `discount` (maior desconto de campanha primeiro) e `newest`.
                 */
                sort?: components["schemas"]["ProductSort"];
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
            /** @description Página de produtos. */
            200: {
                headers: {
                    "X-Request-ID": components["headers"]["RequestId"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["ProductPage"];
                };
            };
            400: components["responses"]["BadRequest"];
            422: components["responses"]["UnprocessableEntity"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    listBrands: {
        parameters: {
            query?: {
                q?: string;
                category?: string;
                min_price_cents?: number;
                max_price_cents?: number;
                on_sale?: boolean;
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
            /** @description Marcas com a quantidade de produtos. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["BrandCount"][];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            422: components["responses"]["UnprocessableEntity"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    getProduct: {
        parameters: {
            query?: never;
            header?: {
                /** @description Identificador da requisição; gerado pelo serviço se ausente e devolvido na resposta. */
                "X-Request-ID"?: components["parameters"]["RequestId"];
            };
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Produto. */
            200: {
                headers: {
                    ETag: components["headers"]["ETag"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Product"];
                };
            };
            404: components["responses"]["NotFound"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    listCategories: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Categorias. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["Category"][];
                    };
                };
            };
            500: components["responses"]["InternalError"];
        };
    };
    listActiveCampaigns: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Campanhas ativas. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["Campaign"][];
                    };
                };
            };
            500: components["responses"]["InternalError"];
        };
    };
    adminListProducts: {
        parameters: {
            query?: {
                /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
                cursor?: components["parameters"]["Cursor"];
                /** @description Itens por página. */
                limit?: components["parameters"]["Limit"];
                status?: components["schemas"]["ProductStatus"];
                /** @description Busca exata por SKU. */
                sku?: string;
                /** @description Busca por nome, marca, descrição e SKU (a mesma da vitrine), em qualquer status. */
                q?: string;
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Página de produtos. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["AdminProduct"][];
                        page: components["schemas"]["PageInfo"];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            500: components["responses"]["InternalError"];
        };
    };
    createProduct: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ProductInput"];
            };
        };
        responses: {
            /** @description Criado. Publica `catalog.product.v1` / `created`. */
            201: {
                headers: {
                    Location: components["headers"]["Location"];
                    ETag: components["headers"]["ETag"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminProduct"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    adminGetProduct: {
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
            /** @description Produto. */
            200: {
                headers: {
                    ETag: components["headers"]["ETag"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminProduct"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    updateProduct: {
        parameters: {
            query?: never;
            header: {
                /** @description ETag da última leitura do recurso (concorrência otimista). */
                "If-Match": components["parameters"]["IfMatch"];
            };
            path: {
                product_id: components["parameters"]["ProductId"];
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ProductInput"];
            };
        };
        responses: {
            /** @description Atualizado. */
            200: {
                headers: {
                    ETag: components["headers"]["ETag"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["AdminProduct"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            412: components["responses"]["PreconditionFailed"];
            422: components["responses"]["UnprocessableEntity"];
            428: components["responses"]["PreconditionRequired"];
            500: components["responses"]["InternalError"];
        };
    };
    deleteProduct: {
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
            /** @description Removido. */
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
    createCategory: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CategoryInput"];
            };
        };
        responses: {
            /** @description Criada. */
            201: {
                headers: {
                    Location: components["headers"]["Location"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Category"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    adminListCampaigns: {
        parameters: {
            query?: {
                /** @description Cursor opaco devolvido em `page.next_cursor`. Omitir para a primeira página. */
                cursor?: components["parameters"]["Cursor"];
                /** @description Itens por página. */
                limit?: components["parameters"]["Limit"];
            };
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Página de campanhas. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["Campaign"][];
                        page: components["schemas"]["PageInfo"];
                    };
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            500: components["responses"]["InternalError"];
        };
    };
    createCampaign: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CampaignInput"];
            };
        };
        responses: {
            /** @description Criada. */
            201: {
                headers: {
                    Location: components["headers"]["Location"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Campaign"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    adminGetCampaign: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Campanha. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Campaign"];
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    updateCampaign: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["CampaignInput"];
            };
        };
        responses: {
            /** @description Atualizada. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Campaign"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
    deleteCampaign: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Removida. */
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
    adminListCampaignProducts: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Produtos da campanha. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": {
                        data: components["schemas"]["AdminProduct"][];
                    };
                };
            };
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    setCampaignProducts: {
        parameters: {
            query?: never;
            header?: never;
            path: {
                campaign_id: components["parameters"]["CampaignId"];
            };
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": {
                    product_ids: string[];
                };
            };
        };
        responses: {
            /** @description Lista substituída. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            403: components["responses"]["Forbidden"];
            404: components["responses"]["NotFound"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            500: components["responses"]["InternalError"];
        };
    };
}
