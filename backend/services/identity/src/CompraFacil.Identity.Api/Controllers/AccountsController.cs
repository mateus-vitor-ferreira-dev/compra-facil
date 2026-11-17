using CompraFacil.Identity.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Identity.Api.Controllers;

/// <summary>Contas (contracts/openapi/identity.v1.yaml).</summary>
[ApiController]
[Produces("application/json")]
public sealed class AccountsController(
    RegisterAccount register,
    AccountQueries queries,
    ConfirmEmail confirm,
    ResendConfirmation resend,
    RequestPasswordReset requestReset,
    ResetPassword reset) : ControllerBase
{
    /// <summary>registerAccount: cadastro de cliente (papel customer). Não faz login: o front chama createSession em seguida.</summary>
    [HttpPost("v1/accounts")]
    public async Task<IActionResult> Register(RegisterRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var account = await register.HandleAsync(new RegisterCommand(body.Email ?? "", body.Password ?? "", body.Name ?? ""), ct);
        return Created("/v1/me", account);
    }

    /// <summary>confirmEmail: o link do e-mail de confirmação (o front repassa a conta e o token).</summary>
    [HttpPost("v1/accounts/confirm-email")]
    public async Task<IActionResult> ConfirmEmail(ConfirmEmailRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await confirm.HandleAsync(new ConfirmEmailCommand(body.AccountId ?? Guid.Empty, body.Token ?? ""), ct);
        return NoContent();
    }

    /// <summary>resendEmailConfirmation: manda outro link. Sempre 202: a resposta não diz se a conta existe.</summary>
    [HttpPost("v1/accounts/confirm-email/resend")]
    public async Task<IActionResult> Resend(ResendConfirmationRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await resend.HandleAsync(new ResendConfirmationCommand(body.Email ?? ""), ct);
        return Accepted();
    }

    /// <summary>requestPasswordReset: manda o link de "esqueci minha senha". Sempre 202: a resposta não diz se a conta existe.</summary>
    [HttpPost("v1/accounts/password-reset")]
    public async Task<IActionResult> RequestPasswordReset(RequestPasswordResetRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await requestReset.HandleAsync(new RequestPasswordResetCommand(body.Email ?? ""), ct);
        return Accepted();
    }

    /// <summary>resetPassword: a senha nova pelo link do e-mail (o front repassa a conta e o token).</summary>
    [HttpPost("v1/accounts/password-reset/confirm")]
    public async Task<IActionResult> ResetPassword(ResetPasswordRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        await reset.HandleAsync(new ResetPasswordCommand(body.AccountId ?? Guid.Empty, body.Token ?? "", body.Password ?? ""), ct);
        return NoContent();
    }

    /// <summary>getMe: a conta dona do access token.</summary>
    [HttpGet("v1/me")]
    [Authorize]
    public async Task<AccountView> Me(CancellationToken ct) =>
        await queries.GetAsync(Guid.Parse(User.Identity!.Name!), ct);
}

public sealed record RegisterRequest(string? Email, string? Password, string? Name);

public sealed record ConfirmEmailRequest(Guid? AccountId, string? Token);

public sealed record ResendConfirmationRequest(string? Email);

public sealed record RequestPasswordResetRequest(string? Email);

public sealed record ResetPasswordRequest(Guid? AccountId, string? Token, string? Password);
