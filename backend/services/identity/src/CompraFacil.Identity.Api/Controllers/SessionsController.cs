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
public sealed class SessionsController(Sessions sessions) : ControllerBase
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

    private void NoStore() => Response.Headers.CacheControl = "no-store";
}

public sealed record LoginRequest(string? Email, string? Password);

public sealed record RefreshRequest(string? RefreshToken);

public sealed record TokenResponse(string AccessToken, string TokenType, int ExpiresIn, string RefreshToken, int RefreshExpiresIn)
{
    public static TokenResponse From(IssuedSession s)
    {
        ArgumentNullException.ThrowIfNull(s);
        return new(s.AccessToken, "Bearer", (int)s.AccessTokenExpiresIn.TotalSeconds, s.RefreshToken, (int)s.RefreshTokenExpiresIn.TotalSeconds);
    }
}
