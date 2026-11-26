using Embrulhado.Identity.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Embrulhado.Identity.Api.Controllers;

/// <summary>
/// Sessões: login, refresh e logout (contracts/openapi/identity.v1.yaml). Respostas com token não vão
/// para cache (Cache-Control: no-store, como no OAuth 2.0). Conta que precisa do segundo fator recebe, no
/// login com senha ou com o Google, 202 com um tíquete (<see cref="MfaChallengeResponse"/>), nunca tokens.
/// </summary>
// login, refresh, Google e convidado: quem chama ainda não tem token (cada ação confere o que recebe)
[AllowAnonymous]
[ApiController]
[Route("v1/sessions")]
[Produces("application/json")]
public sealed class SessionsController(Sessions sessions, GoogleSignIn google, TwoFactorSignIn twoFactor) : ControllerBase
{
    /// <summary>createSession: login com e-mail e senha; 202 com tíquete quando falta o segundo fator.</summary>
    [HttpPost]
    public async Task<IActionResult> Login(LoginRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return Outcome(await sessions.LoginAsync(new LoginCommand(body.Email ?? "", body.Password ?? ""), ct));
    }

    /// <summary>verifySessionMfa: o segundo passo do login (código do autenticador ou de recuperação).</summary>
    [HttpPost("mfa")]
    public async Task<TokenResponse> VerifyMfa(MfaVerifyRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await twoFactor.VerifyAsync(body.Ticket ?? "", body.Code, body.RecoveryCode, ct));
    }

    /// <summary>startMfaSetup: a chave e o URI do QR code para o operador cadastrar o autenticador.</summary>
    [HttpPost("mfa/setup")]
    public async Task<MfaSetupResponse> StartMfaSetup(MfaTicketRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        var setup = await twoFactor.BeginSetupAsync(body.Ticket ?? "", ct);
        return new MfaSetupResponse(setup.SharedKey, setup.AuthenticatorUri);
    }

    /// <summary>confirmMfaSetup: o primeiro código liga a 2FA; a resposta traz a sessão e os códigos de recuperação.</summary>
    [HttpPost("mfa/setup/confirm")]
    public async Task<MfaEnabledResponse> ConfirmMfaSetup(MfaConfirmRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        var enabled = await twoFactor.ConfirmSetupAsync(body.Ticket ?? "", body.Code ?? "", ct);
        return MfaEnabledResponse.From(enabled);
    }

    /// <summary>refreshSession: troca o refresh token por um par novo (o enviado deixa de valer).</summary>
    [HttpPost("refresh")]
    public async Task<TokenResponse> Refresh(RefreshRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await sessions.RefreshAsync(body.RefreshToken ?? "", ct));
    }

    /// <summary>revokeSession: logout; o refresh token e os trocados a partir do mesmo login deixam de valer.</summary>
    [HttpPost("revoke")]
    public async Task<IActionResult> Revoke(RefreshRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await sessions.RevokeAsync(body.RefreshToken ?? "", ct);
        return NoContent();
    }

    /// <summary>
    /// createGoogleSession: login (ou cadastro) com o ID token do Google. E-mail que já tem conta com
    /// senha dá 409: o vínculo exige a senha (linkGoogleSession).
    /// </summary>
    [HttpPost("google")]
    public async Task<IActionResult> Google(GoogleSessionRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return Outcome(await google.SignInAsync(body.IdToken ?? "", body.Nonce ?? "", ct));
    }

    /// <summary>linkGoogleSession: liga o Google à conta do mesmo e-mail, conferindo a senha dela.</summary>
    [HttpPost("google/link")]
    public async Task<IActionResult> LinkGoogle(GoogleLinkRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return Outcome(await google.LinkAsync(body.IdToken ?? "", body.Nonce ?? "", body.Password ?? "", ct));
    }

    /// <summary>
    /// createGuestSession: compra sem conta. Cada compra é uma conta própria; a mesma chave do formulário
    /// devolve a mesma, e o e-mail digitado não dá acesso a nada que já exista com ele.
    /// </summary>
    [HttpPost("guest")]
    public async Task<TokenResponse> Guest(GuestSessionRequest body, [FromServices] GuestCheckout guests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await guests.StartAsync(
            new StartGuestCommand(body.Email ?? "", body.Name ?? "", body.CheckoutKey ?? Guid.Empty), ct));
    }

    /// <summary>accessGuestSession: abre a sessão pelo link do e-mail; o link continua valendo até vencer.</summary>
    [HttpPost("guest/access")]
    public async Task<TokenResponse> GuestAccess(GuestAccessRequest body, [FromServices] GuestCheckout guests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await guests.AccessAsync(body.Token ?? "", ct));
    }

    /// <summary>resendGuestAccess: manda o link de novo. Sempre 202: a resposta não diz se houve compra.</summary>
    [HttpPost("guest/access/resend")]
    public async Task<IActionResult> ResendGuestAccess(GuestAccessResendRequest body, [FromServices] GuestCheckout guests, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await guests.ResendAsync(body.Email ?? "", ct);
        return Accepted();
    }

    private void NoStore() => Response.Headers.CacheControl = "no-store";

    // 200 com os tokens, ou 202 com o tíquete do segundo fator
    private IActionResult Outcome(SignInOutcome outcome) => outcome.Session is { } session
        ? Ok(TokenResponse.From(session))
        : StatusCode(StatusCodes.Status202Accepted, MfaChallengeResponse.From(outcome.Challenge!));
}

public sealed record MfaTicketRequest(string? Ticket);

public sealed record MfaVerifyRequest(string? Ticket, string? Code, string? RecoveryCode);

public sealed record MfaConfirmRequest(string? Ticket, string? Code);

/// <summary>O primeiro fator conferiu; a sessão depende do segundo. <c>status</c>: mfa_required ou mfa_setup_required.</summary>
public sealed record MfaChallengeResponse(string Status, string MfaTicket, int ExpiresIn)
{
    public static MfaChallengeResponse From(MfaChallenge c)
    {
        ArgumentNullException.ThrowIfNull(c);
        return new(c.Status, c.Ticket, (int)c.ExpiresIn.TotalSeconds);
    }
}

public sealed record MfaSetupResponse(string SharedKey, string AuthenticatorUri);

public sealed record MfaEnabledResponse(
    string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, int RefreshExpiresIn, IReadOnlyList<string> RecoveryCodes)
{
    public static MfaEnabledResponse From(EnabledTwoFactor e)
    {
        ArgumentNullException.ThrowIfNull(e);
        var t = TokenResponse.From(e.Session);
        return new(t.AccessToken, t.TokenType, t.ExpiresIn, t.RefreshToken, t.RefreshExpiresIn, e.RecoveryCodes);
    }
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record GoogleSessionRequest(string? IdToken, string? Nonce);

public sealed record GuestSessionRequest(string? Email, string? Name, Guid? CheckoutKey);

public sealed record GuestAccessRequest(string? Token);

public sealed record GuestAccessResendRequest(string? Email);

public sealed record GoogleLinkRequest(string? IdToken, string? Nonce, string? Password);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, int RefreshExpiresIn)
{
    public static TokenResponse From(IssuedSession s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.AccessToken, "Bearer", (int)s.AccessTokenExpiresIn.TotalSeconds, s.RefreshToken, (int)s.RefreshTokenExpiresIn.TotalSeconds);
    }
}
