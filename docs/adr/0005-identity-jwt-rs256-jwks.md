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
  esticada até a sucessora começar, que ainda sai publicada um dia antes. Se ficou fora tanto que a chave
  venceu, a mais recente volta a assinar por um dia (os serviços já a têm em cache) enquanto a sucessora é
  publicada. Chave que assina sem ter sido anunciada, só na primeira subida, quando não há token ainda. A regra é uma função pura (`SigningKeySchedule`), com testes de
  unidade e um teste de integração que avança o relógio um mês.
- **As chaves ficam no banco do identity**, com a privada cifrada pelo Data Protection do ASP.NET. O anel de
  chaves do Data Protection fica no mesmo banco, para que todas as réplicas decifrem. A criação da próxima
  chave acontece sob um advisory lock do Postgres, para duas réplicas não criarem duas chaves.
- **Refresh token opaco e rotativo** (30 dias): cada uso troca o token por um novo da mesma família, e o banco
  guarda só o SHA-256. Um token já trocado, apresentado de novo, **derruba a família inteira**, porque só quem
  o copiou o teria. Refresh e logout da mesma sessão passam por um advisory lock da família, e um logout no
  meio de um refresh também derruba o token que esse refresh acabou de criar.
- **Intervalo de reuso** (#173, 10 s, `Identity:Tokens:RefreshReuseInterval`): logo depois da troca, o token
  anterior ainda dá um par novo da mesma família (um irmão), em vez de derrubar a sessão, desde que a sessão
  continue aberta (sem logout nem reuso). Duas requisições do mesmo navegador (a página e um prefetch) caem
  em instâncias diferentes do BFF na Vercel e renovam o mesmo token juntas; sem o intervalo, uma ganhava e a
  outra deslogava o cliente. A próxima troca normal revoga os irmãos que sobraram, e a sessão volta a ter um
  token só. Fora da janela, vale a detecção de reuso. É o "reuse interval" do Auth0; a alternativa, um lock
  distribuído no BFF (Redis/Upstash), poria infraestrutura nova no front para resolver o que o emissor
  resolve sozinho.
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
- Um token com `kid` desconhecido é recusado na primeira vez pelos serviços .NET (o JwtBearer busca o JWKS
  de novo em segundo plano) e aceito na hora pelo catalog (o keyfunc busca antes de responder). Pelo desenho
  da rotação, isso só acontece na primeira chave do identity, quando ainda não há token em circulação.
- Logout não invalida o access token já emitido; ele vale até vencer (15 min). Para revogação imediata seria
  preciso uma lista de bloqueio consultada a cada requisição, que é o acoplamento que o JWT evita.
- O BFF do front (#148) guarda o refresh token em cookie `HttpOnly` e faz um refresh por vez em cada
  instância; entre instâncias, o intervalo de reuso mantém a sessão.
- O intervalo de reuso é a janela em que um token copiado ainda passa: quem o roubou nos 10 s seguintes à
  troca ganha um par da sessão. Fora dela, o roubo é detectado como antes. Zero desliga o intervalo.
- Confirmação de e-mail (#168): o identity publica `AccountRegistered` e `EmailConfirmationRequested` pelo
  outbox, na mesma transação do cadastro, e o notifications manda o link. Exigir a confirmação para entrar é
  configuração (`Identity:EmailConfirmation:Required`), desligada no ambiente local.
- A validade de cada chave no JWKS é fixada quando ela é criada: aumentar a validade do access token em
  produção só vale por completo para as chaves criadas depois.
- O anel do Data Protection no mesmo banco das chaves protege contra vazamento de backup parcial, não do banco
  inteiro. Em produção (F9), o anel deve ser protegido por uma chave fora do banco (certificado ou KMS).
- **Login com o Google** (#180): o BFF faz o authorization code com PKCE e entrega o ID token ao identity
  (`POST /v1/sessions/google`), que o valida pelo JWKS do Google (emissor, `aud` igual ao client id,
  validade, `nonce`, `email_verified`) e abre uma sessão própria, com os mesmos tokens do login por senha.
  A conta é a do `sub` do Google (`user_logins`), não a do e-mail; um e-mail que já tem conta com senha só
  é ligado ao Google depois de confirmar a senha (`/google/link`). O resto do sistema não muda: os serviços
  continuam validando só os tokens do identity.
