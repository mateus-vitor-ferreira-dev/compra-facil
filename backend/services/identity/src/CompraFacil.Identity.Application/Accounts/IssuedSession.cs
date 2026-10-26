namespace CompraFacil.Identity.Application.Accounts;

/// <summary>O que o login e o refresh devolvem.</summary>
public sealed record IssuedSession(string AccessToken, TimeSpan AccessTokenExpiresIn, string RefreshToken, TimeSpan RefreshTokenExpiresIn);

/// <summary>423: bloqueio temporário depois de várias senhas erradas.</summary>
public sealed class AccountLockedException(TimeSpan retryAfter)
    : BuildingBlocks.Web.ApplicationProblemException(
        Microsoft.AspNetCore.Http.StatusCodes.Status423Locked, "Conta bloqueada",
        "muitas tentativas de login com senha errada; tente de novo mais tarde", retryAfter);
