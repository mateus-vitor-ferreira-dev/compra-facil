// Gerado de contracts/openapi/identity.v1.yaml por scripts/generate-api-types.mjs. Não edite à mão.
export interface paths {
    "/v1/accounts": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Cadastra um cliente
         * @description Cria a conta com o papel `customer`. Não faz login: em seguida, chame `createSession`.
         *     Senha: de 8 a 128 caracteres, com pelo menos 4 diferentes. Sem regras de composição (NIST SP 800-63B).
         */
        post: operations["registerAccount"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/accounts/confirm-email": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Confirma o e-mail pelo link
         * @description O link do e-mail leva para a página do front com `conta` e `token` na query; o front os manda aqui.
         *     Conta já confirmada: `204` também (o link pode ser aberto duas vezes). Conta inexistente, token
         *     errado ou vencido (1 dia): o mesmo `422`.
         */
        post: operations["confirmEmail"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/accounts/confirm-email/resend": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Manda outro link de confirmação
         * @description Sempre `202`, exista a conta ou não, já confirmada ou não: a resposta não revela quem tem conta.
         *     O link anterior continua valendo até vencer.
         */
        post: operations["resendEmailConfirmation"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/me": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /** A conta dona do access token */
        get: operations["getMe"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Login com e-mail e senha
         * @description E-mail inexistente e senha errada dão o mesmo `401` (e o hash de senha roda nos dois casos). Na
         *     quinta senha errada seguida, a conta fica bloqueada para login por 15 minutos (`423`, com
         *     `Retry-After`); sessões já abertas continuam valendo. Como o cadastro (`409`), o `423` revela que
         *     a conta existe: o rate limit do gateway é o que limita quem tenta descobrir.
         */
        post: operations["createSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/google": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Login (ou cadastro) com a conta Google
         * @description Recebe o ID token que o BFF obteve do Google (authorization code com PKCE) e o `nonce` que ele
         *     gerou para este login. O identity valida a assinatura pelo JWKS do Google, o emissor, o `aud`
         *     (o client id da loja), a validade, o `nonce` e `email_verified`.
         *
         *     A conta é achada pelo `sub` do Google, não pelo e-mail: trocar o e-mail no Google não muda a
         *     conta. Sem conta, cria uma já confirmada (sem senha), com o papel `customer`. Se o e-mail já tem
         *     conta com senha, responde `409` (título "Conta já existe"): o vínculo é por `linkGoogleSession`,
         *     com a senha, para que um Google com o mesmo e-mail não entre na conta de outra pessoa.
         */
        post: operations["createGoogleSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/google/link": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Vincula o Google à conta do mesmo e-mail, com a senha dela
         * @description Valida o ID token como `createGoogleSession`, confere a senha da conta do mesmo e-mail (com o
         *     bloqueio por tentativas do login) e liga o `sub` do Google a ela. Daí em diante, o Google sozinho
         *     entra nela.
         */
        post: operations["linkGoogleSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/refresh": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Troca o refresh token por um par novo
         * @description O refresh token enviado deixa de valer; guarde o novo. Token inválido, expirado, revogado ou já
         *     trocado dá `401`. Um token já trocado, apresentado de novo, derruba a sessão inteira, a não ser
         *     dentro do intervalo de reuso (`Identity:Tokens:RefreshReuseInterval`, 10 s): logo depois da troca,
         *     ele ainda dá um par novo da mesma sessão (dois refresh simultâneos com o mesmo token passam). A
         *     próxima troca normal invalida os pares que sobraram.
         */
        post: operations["refreshSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/revoke": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Logout
         * @description Revoga o refresh token e todos os trocados a partir do mesmo login. Idempotente: token
         *     desconhecido ou já revogado também dá `204`. O access token já emitido vale até expirar (15 min).
         */
        post: operations["revokeSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/.well-known/jwks.json": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Chaves públicas de assinatura (JWKS, RFC 7517)
         * @description A anterior (até os tokens dela vencerem), a atual e a próxima (um dia antes de assinar).
         */
        get: operations["getJwks"];
        put?: never;
        post?: never;
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/.well-known/openid-configuration": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        /**
         * Descoberta do emissor (subconjunto do OpenID Connect Discovery)
         * @description Só o que a validação de JWT usa. Bibliotecas como o JwtBearer do .NET acham o JWKS por aqui.
         */
        get: operations["getOpenIdConfiguration"];
        put?: never;
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
         * Format: email
         * @example ana@exemplo.com
         */
        Email: string;
        RegisterRequest: {
            email: components["schemas"]["Email"];
            password: string;
            /** @example Ana Souza */
            name: string;
        };
        GoogleSessionRequest: {
            /** @description O ID token (JWT) que o Google devolveu ao BFF. */
            id_token: string;
            /** @description O nonce que o BFF gerou para este login (vem dentro do ID token). */
            nonce: string;
        };
        GoogleLinkRequest: {
            id_token: string;
            nonce: string;
            password: string;
        };
        LoginRequest: {
            email: components["schemas"]["Email"];
            password: string;
        };
        RefreshRequest: {
            refresh_token: string;
        };
        /**
         * @description `customer`: compra · `admin`: rotas `/v1/admin/*` e painel administrativo.
         * @enum {string}
         */
        Role: "customer" | "admin";
        Account: {
            /**
             * Format: uuid
             * @description O mesmo valor do claim `sub` do access token.
             */
            id: string;
            email: components["schemas"]["Email"];
            name: string;
            email_confirmed: boolean;
            roles: components["schemas"]["Role"][];
            /** Format: date-time */
            created_at: string;
        };
        /** @description Resposta no formato do OAuth 2.0 (RFC 6749, seção 5.1). */
        Tokens: {
            /** @description JWT RS256; vai em `Authorization: Bearer` nas outras APIs. */
            access_token: string;
            /** @constant */
            token_type: "Bearer";
            /**
             * @description Segundos até o access token vencer.
             * @example 900
             */
            expires_in: number;
            /** @description Opaco. Use uma vez em `refreshSession`; a resposta traz o próximo. */
            refresh_token: string;
            /**
             * @description Segundos até o refresh token vencer, se não for usado antes.
             * @example 2592000
             */
            refresh_expires_in: number;
        };
        Jwk: {
            /** @constant */
            kty: "RSA";
            /** @constant */
            use: "sig";
            /** @constant */
            alg: "RS256";
            /** @description Igual ao `kid` do header dos tokens assinados com esta chave. */
            kid: string;
            /** @description Módulo, em base64url. */
            n: string;
            /** @description Expoente, em base64url. */
            e: string;
        };
        Jwks: {
            keys: components["schemas"]["Jwk"][];
        };
        OpenIdConfiguration: {
            /** @example compra-facil-identity */
            issuer: string;
            /** Format: uri */
            jwks_uri: string;
            id_token_signing_alg_values_supported: "RS256"[];
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
        /** @description Access token e refresh token. */
        Tokens: {
            headers: {
                /** @description `no-store`: resposta com credencial não vai para cache. */
                "Cache-Control"?: "no-store";
                [name: string]: unknown;
            };
            content: {
                "application/json": components["schemas"]["Tokens"];
            };
        };
        /** @description Requisição malformada (JSON inválido, parâmetro com tipo errado). */
        BadRequest: {
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
        /** @description Token ausente, inválido ou expirado. */
        Unauthorized: {
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
    };
    parameters: never;
    requestBodies: never;
    headers: {
        /** @description Segundos até poder tentar de novo. */
        RetryAfter: number;
        /** @description As chaves podem ficar em cache por 5 minutos. */
        CacheControl: string;
        /** @description URL do recurso criado. */
        Location: string;
    };
    pathItems: never;
}
export type $defs = Record<string, never>;
export interface operations {
    registerAccount: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RegisterRequest"];
            };
        };
        responses: {
            /** @description Conta criada. */
            201: {
                headers: {
                    Location: components["headers"]["Location"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Account"];
                };
            };
            400: components["responses"]["BadRequest"];
            409: components["responses"]["Conflict"];
            422: components["responses"]["UnprocessableEntity"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    confirmEmail: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": {
                    /** Format: uuid */
                    account_id: string;
                    token: string;
                };
            };
        };
        responses: {
            /** @description E-mail confirmado. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            400: components["responses"]["BadRequest"];
            422: components["responses"]["UnprocessableEntity"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    resendEmailConfirmation: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": {
                    email: components["schemas"]["Email"];
                };
            };
        };
        responses: {
            /** @description Se a conta existe e não está confirmada, o e-mail sai em instantes. */
            202: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            400: components["responses"]["BadRequest"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    getMe: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Conta. */
            200: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Account"];
                };
            };
            401: components["responses"]["Unauthorized"];
            404: components["responses"]["NotFound"];
            500: components["responses"]["InternalError"];
        };
    };
    createSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["LoginRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description Senha certa, mas o e-mail ainda não foi confirmado (só com a confirmação exigida). */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            422: components["responses"]["UnprocessableEntity"];
            /** @description Conta bloqueada temporariamente por senhas erradas. */
            423: {
                headers: {
                    "Retry-After": components["headers"]["RetryAfter"];
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    createGoogleSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GoogleSessionRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description O e-mail já tem conta com senha; vincule com `linkGoogleSession`. */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    linkGoogleSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GoogleLinkRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description Conta bloqueada temporariamente por senhas erradas. */
            423: {
                headers: {
                    "Retry-After": components["headers"]["RetryAfter"];
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    refreshSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RefreshRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    revokeSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RefreshRequest"];
            };
        };
        responses: {
            /** @description Sessão encerrada. */
            204: {
                headers: {
                    [name: string]: unknown;
                };
                content?: never;
            };
            400: components["responses"]["BadRequest"];
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    getJwks: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Chaves. */
            200: {
                headers: {
                    "Cache-Control": components["headers"]["CacheControl"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["Jwks"];
                };
            };
            500: components["responses"]["InternalError"];
        };
    };
    getOpenIdConfiguration: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody?: never;
        responses: {
            /** @description Emissor e endereço do JWKS. */
            200: {
                headers: {
                    "Cache-Control": components["headers"]["CacheControl"];
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["OpenIdConfiguration"];
                };
            };
            500: components["responses"]["InternalError"];
        };
    };
}
