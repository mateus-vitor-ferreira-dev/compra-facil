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
    "/v1/accounts/password-reset": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Pede o link para criar uma senha nova
         * @description Sempre `202`, exista a conta ou não: a resposta não revela quem tem conta. Se o e-mail é de uma
         *     conta (com ou sem senha: quem só entrou pelo Google também pode criar uma), o link sai por e-mail
         *     e vale 1 hora. Compras sem conta não têm senha e não recebem nada. Cada pedido gera outro link; os
         *     anteriores continuam valendo até vencer ou até a senha ser trocada.
         */
        post: operations["requestPasswordReset"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/accounts/password-reset/confirm": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Cria a senha nova pelo link
         * @description O link do e-mail leva para a página do front com `conta` e `token` na query; o front os manda aqui
         *     com a senha nova (mesmas regras do cadastro). Troca a senha, confirma o e-mail (o link prova que
         *     a pessoa o lê), desbloqueia a conta e revoga todos os refresh tokens: as sessões abertas caem.
         *     O token vale uma vez só. Conta inexistente, token errado, já usado ou vencido: o mesmo `422` em
         *     `token`; senha fora das regras: `422` em `password`.
         */
        post: operations["resetPassword"];
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
    "/v1/me/two-factor/recovery-codes": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Gera códigos de recuperação novos
         * @description Para a conta com 2FA ativa, numa sessão que passou pelo segundo fator (`amr` com `mfa`; sem ele,
         *     `403`), com um código atual do app autenticador. Os 10 códigos novos aparecem só nesta resposta e os
         *     anteriores deixam de valer. Código errado dá `422` e conta como senha errada.
         */
        post: operations["regenerateRecoveryCodes"];
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
         *
         *     Conta com 2FA, ou com o papel `admin`, responde `202` com o tíquete do segundo fator, nunca tokens.
         */
        post: operations["createSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/guest": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Compra sem conta
         * @description Cria uma conta própria para esta compra, sem senha e com o papel `customer`, e abre a sessão. A
         *     mesma `checkout_key` com o mesmo e-mail devolve a mesma conta nos 30 minutos depois de a compra
         *     começar: o reenvio do formulário não duplica o pedido. Depois, dá `409`, e a compra se abre só pelo
         *     link enviado ao e-mail. O e-mail digitado não é verificado e não dá acesso a nada que já exista com ele.
         */
        post: operations["createGuestSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/guest/access": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Abre a sessão pelo link enviado por e-mail
         * @description O link continua valendo até vencer. Link desconhecido e vencido dão o mesmo `401`.
         */
        post: operations["accessGuestSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/guest/access/resend": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Manda de novo o link das compras sem conta
         * @description Sempre `202`, com compra ou sem compra naquele e-mail. O mesmo link não é reenviado em intervalos curtos.
         */
        post: operations["resendGuestAccess"];
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
         *
         *     O Google é só o primeiro fator: conta com 2FA, ou com o papel `admin`, recebe `202` com o tíquete,
         *     como no login com senha.
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
         *     entra nela (menos na conta com 2FA ou de operador, que recebe `202` com o tíquete do segundo fator).
         */
        post: operations["linkGoogleSession"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/mfa": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Segundo passo do login (código do autenticador ou de recuperação)
         * @description Recebe o `mfa_ticket` do `202` do login e **um** dos dois: `code`, os 6 dígitos do app autenticador
         *     (o passo de 30 s atual, o anterior ou o seguinte; um código já usado não vale de novo), ou
         *     `recovery_code`, um dos códigos de recuperação (cada um vale uma vez). O código certo gasta o tíquete
         *     e abre a sessão, com `amr` = `["mfa"]` no access token.
         *
         *     Tíquete desconhecido, vencido, já usado ou de antes de uma troca de senha dá `401`: volte ao login.
         *     Código errado dá `422` e conta como senha errada; o quinto erro seguido bloqueia a conta (`423`).
         */
        post: operations["verifySessionMfa"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/mfa/setup": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Chave e QR code para cadastrar o app autenticador
         * @description Com o tíquete `mfa_setup_required` do login do operador sem 2FA. Cada chamada gera uma chave nova
         *     (a anterior deixa de servir); ela só passa a valer com o primeiro código certo em `confirmMfaSetup`.
         *     O tíquete não é gasto aqui.
         */
        post: operations["startMfaSetup"];
        delete?: never;
        options?: never;
        head?: never;
        patch?: never;
        trace?: never;
    };
    "/v1/sessions/mfa/setup/confirm": {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        get?: never;
        put?: never;
        /**
         * Liga a 2FA com o primeiro código e abre a sessão
         * @description O primeiro código certo do app liga a 2FA, gasta o tíquete e devolve a sessão (com `amr`) e 10
         *     códigos de recuperação de uso único. Os códigos aparecem só nesta resposta: o identity guarda só o
         *     hash deles. Código errado dá `422` e conta como senha errada.
         */
        post: operations["confirmMfaSetup"];
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
         *
         *     A sessão aberta sem o segundo fator de uma conta que agora o exige (2FA ligada depois, ou a conta
         *     virou operador) não renova: `401`, e a pessoa entra de novo com o código.
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
        /**
         * @description Nome de quem compra; vai na saudação dos e-mails da loja. Só letras (com acento), espaços,
         *     apóstrofo, hífen e ponto. Nada de link, e-mail ou domínio: `://`, `www.`, `@`, `/`, `:` e
         *     palavras como `exemplo.com` dão 422 ("use só letras, espaços, apóstrofo, hífen e ponto").
         *     Espaços nas pontas saem e os repetidos viram um só.
         * @example Ana Souza
         * @example João d'Ávila
         * @example Maria-Clara
         * @example J. R. Silva
         */
        PersonName: string;
        RegisterRequest: {
            email: components["schemas"]["Email"];
            password: string;
            name: components["schemas"]["PersonName"];
        };
        ResetPasswordRequest: {
            /** Format: uuid */
            account_id: string;
            token: string;
            password: string;
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
         * @description `customer`: compra · `admin`: rotas `/v1/admin/*` e painel administrativo (com 2FA) · `auditor`: leitura da operação (robô).
         * @enum {string}
         */
        Role: "customer" | "admin" | "auditor";
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
            /** @description Comprou sem conta; o acesso é pelo link enviado por e-mail, sem senha. */
            is_guest?: boolean;
            /** @description A autenticação em duas etapas (app autenticador) está ativa. */
            two_factor_enabled?: boolean;
            /** @description Quantos códigos de recuperação ainda valem; nulo sem 2FA. */
            recovery_codes_left?: number | null;
        };
        GuestSessionRequest: {
            email: components["schemas"]["Email"];
            name: components["schemas"]["PersonName"];
            /**
             * Format: uuid
             * @description Nasce com o formulário do checkout e é a mesma em cada reenvio dele.
             */
            checkout_key: string;
        };
        GuestAccessRequest: {
            token: string;
        };
        GuestAccessResendRequest: {
            email: components["schemas"]["Email"];
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
        /** @description A senha (ou o Google) conferiu; a sessão depende do segundo fator. */
        MfaChallenge: {
            /**
             * @description `mfa_required`: mande o código em `verifySessionMfa`. `mfa_setup_required`: operador sem 2FA;
             *     cadastre o app autenticador (`startMfaSetup`, `confirmMfaSetup`).
             * @enum {string}
             */
            status: "mfa_required" | "mfa_setup_required";
            /** @description Opaco, de uso único. Não é token de API nem refresh token. */
            mfa_ticket: string;
            /**
             * @description Segundos até o tíquete vencer.
             * @example 300
             */
            expires_in: number;
        };
        MfaTicketRequest: {
            ticket: string;
        };
        /** @description `ticket` e um dos dois: `code` ou `recovery_code`. */
        MfaVerifyRequest: {
            ticket: string;
            /**
             * @description Os 6 dígitos do app autenticador.
             * @example 123456
             */
            code?: string;
            /** @example k7m2p-9xq4r */
            recovery_code?: string;
        };
        MfaConfirmRequest: {
            ticket: string;
            code: string;
        };
        MfaSetup: {
            /** @description A chave em Base32, em grupos de 4, para digitar no app sem o QR code. */
            shared_key: string;
            /** @description O conteúdo do QR code: `otpauth://totp/Embrulhado:<e-mail>?secret=...&issuer=Embrulhado`. */
            authenticator_uri: string;
        };
        MfaEnabled: components["schemas"]["Tokens"] & {
            recovery_codes: components["schemas"]["RecoveryCodeList"];
        };
        RecoveryCodesRequest: {
            code: string;
        };
        RecoveryCodes: {
            recovery_codes: components["schemas"]["RecoveryCodeList"];
        };
        /** @description Códigos de uso único; aparecem só uma vez (o identity guarda só o hash). */
        RecoveryCodeList: string[];
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
        /** @description Falta o segundo fator; nenhum token. */
        MfaChallenge: {
            headers: {
                /** @description `no-store`: resposta com credencial não vai para cache. */
                "Cache-Control"?: "no-store";
                [name: string]: unknown;
            };
            content: {
                "application/json": components["schemas"]["MfaChallenge"];
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
    requestPasswordReset: {
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
            /** @description Se a conta existe, o e-mail sai em instantes. */
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
    resetPassword: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["ResetPasswordRequest"];
            };
        };
        responses: {
            /** @description Senha trocada. O front leva o cliente ao login. */
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
    regenerateRecoveryCodes: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["RecoveryCodesRequest"];
            };
        };
        responses: {
            /** @description Os códigos novos. */
            200: {
                headers: {
                    /** @description `no-store`: resposta com credencial não vai para cache. */
                    "Cache-Control"?: "no-store";
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["RecoveryCodes"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description A sessão não passou pelo segundo fator. */
            403: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            /** @description A conta não tem a 2FA ativa. */
            409: {
                headers: {
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
            422: components["responses"]["UnprocessableEntity"];
            /** @description Conta bloqueada temporariamente por tentativas erradas. */
            423: {
                headers: {
                    "Retry-After": components["headers"]["RetryAfter"];
                    [name: string]: unknown;
                };
                content: {
                    "application/problem+json": components["schemas"]["Problem"];
                };
            };
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
            202: components["responses"]["MfaChallenge"];
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
    createGuestSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GuestSessionRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            /**
             * @description A mesma `checkout_key` já foi usada com outro e-mail, chegou ao mesmo tempo que outra, ou começou
             *     a compra há mais de 30 minutos (o acesso a ela é pelo link do e-mail).
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
            429: components["responses"]["TooManyRequests"];
            500: components["responses"]["InternalError"];
        };
    };
    accessGuestSession: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GuestAccessRequest"];
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
    resendGuestAccess: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["GuestAccessResendRequest"];
            };
        };
        responses: {
            /** @description Aceito. */
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
            202: components["responses"]["MfaChallenge"];
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
            202: components["responses"]["MfaChallenge"];
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
    verifySessionMfa: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MfaVerifyRequest"];
            };
        };
        responses: {
            200: components["responses"]["Tokens"];
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            422: components["responses"]["UnprocessableEntity"];
            /** @description Conta bloqueada temporariamente por códigos (ou senhas) errados. */
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
    startMfaSetup: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MfaTicketRequest"];
            };
        };
        responses: {
            /** @description A chave e o URI do QR code. */
            200: {
                headers: {
                    /** @description `no-store`: resposta com segredo não vai para cache. */
                    "Cache-Control"?: "no-store";
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MfaSetup"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            /** @description Conta bloqueada temporariamente por tentativas erradas. */
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
    confirmMfaSetup: {
        parameters: {
            query?: never;
            header?: never;
            path?: never;
            cookie?: never;
        };
        requestBody: {
            content: {
                "application/json": components["schemas"]["MfaConfirmRequest"];
            };
        };
        responses: {
            /** @description 2FA ativa; a sessão e os códigos de recuperação. */
            200: {
                headers: {
                    /** @description `no-store`: resposta com credencial não vai para cache. */
                    "Cache-Control"?: "no-store";
                    [name: string]: unknown;
                };
                content: {
                    "application/json": components["schemas"]["MfaEnabled"];
                };
            };
            400: components["responses"]["BadRequest"];
            401: components["responses"]["Unauthorized"];
            422: components["responses"]["UnprocessableEntity"];
            /** @description Conta bloqueada temporariamente por tentativas erradas. */
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
