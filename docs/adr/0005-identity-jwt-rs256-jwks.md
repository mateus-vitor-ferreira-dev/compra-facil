# ADR 0005 — Serviço identity: JWT RS256 com rotação de chave e JWKS

- **Status:** aceito
- **Data:** 2026-10-26
- **Issue:** #143

## Contexto

Até a v1.0.0 não havia conta de cliente: o pedido recebia `customer_id` no corpo, e o operador usava um JWT
HS256 emitido por script (`task token`), com um segredo compartilhado por todos os serviços. Isso não serve
para o front (F8). O cliente precisa se cadastrar e fazer login, e cada serviço precisa saber quem chama,
**sem** consultar um serviço central a cada requisição (o que criaria um ponto único de falha e de latência
no caminho da vitrine).

Com HS256, quem valida o token também consegue emitir: cada serviço teria o segredo, e um vazamento em
qualquer um deles bastaria para forjar um admin.

## Decisão

Um serviço **identity** (.NET, no template dos outros) emite os tokens. Os demais só validam.

- **Contas pelo ASP.NET Core Identity**: hash de senha (PBKDF2), bloqueio por tentativas (5 erros, 15 min) e
  e-mail único. A política de senha é só comprimento (8 a 128) e variedade mínima, como recomenda o NIST
  SP 800-63B, sem exigir maiúscula ou símbolo.
- **Access token JWT RS256** de 15 min, com `sub`, `email`, `name`, `roles`, `iss` e `aud`. Assinado pela
  chave privada, que só o identity tem; os outros validam com a pública, publicada em
  `/.well-known/jwks.json` (e descoberta por `/.well-known/openid-configuration`).
- **Rotação de chave pelo relógio**: cada chave assina por 30 dias. A sucessora entra no JWKS **um dia antes**
  de assinar, então quem guarda o JWKS em cache já a conhece quando o primeiro token dela chega; a antiga fica
  publicada até o último token dela vencer. Se o serviço ficou fora na janela de troca, a chave atual é
  esticada até a sucessora começar, que ainda sai publicada um dia antes. A regra é uma função pura (`SigningKeySchedule`), com testes de
  unidade e um teste de integração que avança o relógio um mês.
- **As chaves ficam no banco do identity**, com a privada cifrada pelo Data Protection do ASP.NET. O anel de
  chaves do Data Protection fica no mesmo banco, para que todas as réplicas decifrem. A criação da próxima
  chave acontece sob um advisory lock do Postgres, para duas réplicas não criarem duas chaves.
- **Refresh token opaco e rotativo** (30 dias): cada uso troca o token por um novo da mesma família, e o banco
  guarda só o SHA-256. Um token já trocado, apresentado de novo, **derruba a família inteira**, porque só quem
  o copiou o teria. Refresh e logout da mesma sessão passam por um advisory lock da família: dois refresh
  simultâneos com o mesmo token viram reuso (o segundo espera o primeiro e encontra o token já trocado), e um
  logout no meio de um refresh também derruba o token que esse refresh acabou de criar.
- **O bloqueio por senha não derruba sessões abertas**: senão, cinco tentativas com o e-mail de alguém
  bastariam para desconectá-lo a cada 15 minutos.
- **Contra tentativa de senha em massa**: o bloqueio é por conta, e o gateway limita login e cadastro por IP
  (5 req/s, rajada de 20), com o 429 em ProblemDetails servido pelo próprio identity.

## Alternativas

**Keycloak (ou outro IdP pronto).** Resolveria tudo isso e mais (OIDC completo, MFA, login social), mas é
mais um sistema para operar no k3s de 24 GB da F9, e esconderia exatamente o que o projeto quer mostrar.
Como os outros serviços só dependem do JWKS e das claims, trocar o identity por um IdP depois não muda
nenhum deles.

**OpenIddict / Duende IdentityServer.** Servidor OAuth/OIDC completo dentro do .NET. O fluxo aqui é
*resource owner password* com um front próprio; o authorization code com PKCE só faria diferença com
clientes de terceiros. A licença do Duende também pesa para um projeto público.

**Chave de assinatura por configuração (segredo do Kubernetes).** Mais simples, mas a rotação vira um
procedimento manual de deploy, que costuma não acontecer.

## Consequências

- Um serviço novo valida tokens com três parâmetros: endereço do identity, `iss` e `aud` (#144: `AddJwtAuth`
  nos serviços .NET, `keyfunc` no catalog). O JWT HS256 de dev e o `cmd/token` saíram; o operador entra pelo
  identity, e os testes assinam com uma chave própria (`TestIdentity`, `authtest`).
- Um token com `kid` desconhecido (chave criada sem aviso, só se o identity ficou fora mais que a folga) é
  recusado na primeira vez enquanto o serviço busca o JWKS de novo; os seguintes passam.
- Logout não invalida o access token já emitido; ele vale até vencer (15 min). Para revogação imediata seria
  preciso uma lista de bloqueio consultada a cada requisição, que é o acoplamento que o JWT evita.
- O BFF do front (#148) guarda o refresh token em cookie `HttpOnly` e faz **um refresh por vez**: dois em
  paralelo com o mesmo token contam como reuso e derrubam a sessão.
- Confirmação de e-mail fica para depois: o Identity já gera o token, mas o envio pede um evento para o
  notifications, e o identity ainda não publica eventos.
- A validade de cada chave no JWKS é fixada quando ela é criada: aumentar a validade do access token em
  produção só vale por completo para as chaves criadas depois.
- O anel do Data Protection no mesmo banco das chaves protege contra vazamento de backup parcial, não do banco
  inteiro. Em produção (F9), o anel deve ser protegido por uma chave fora do banco (certificado ou KMS).
