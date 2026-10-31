using CompraFacil.Identity.Application.Accounts;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Identity.Api.Controllers;

/// <summary>
/// Sessões: login, refresh e logout (contracts/openapi/identity.v1.yaml). Respostas com token não vão
/// para cache (Cache-Control: no-store, como no OAuth 2.0).
/// </summary>
[ApiController]
[Route("v1/sessions")]
[Produces("application/json")]
public sealed class SessionsController(Sessions sessions, GoogleSignIn google) : ControllerBase
{
    /// <summary>createSession: login com e-mail e senha.</summary>
    [HttpPost]
    public async Task<TokenResponse> Login(LoginRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await sessions.LoginAsync(new LoginCommand(body.Email ?? "", body.Password ?? ""), ct));
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
    public async Task<TokenResponse> Google(GoogleSessionRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await google.SignInAsync(body.IdToken ?? "", body.Nonce ?? "", ct));
    }

    /// <summary>linkGoogleSession: liga o Google à conta do mesmo e-mail, conferindo a senha dela.</summary>
    [HttpPost("google/link")]
    public async Task<TokenResponse> LinkGoogle(GoogleLinkRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        NoStore();
        return TokenResponse.From(await google.LinkAsync(body.IdToken ?? "", body.Nonce ?? "", body.Password ?? "", ct));
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
