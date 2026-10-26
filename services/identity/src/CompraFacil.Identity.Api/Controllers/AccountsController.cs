using CompraFacil.Identity.Application.Accounts;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace CompraFacil.Identity.Api.Controllers;

/// <summary>Contas (contracts/openapi/identity.v1.yaml).</summary>
[ApiController]
[Produces("application/json")]
public sealed class AccountsController(RegisterAccount register, AccountQueries queries) : ControllerBase
{
    /// <summary>registerAccount: cadastro de cliente (papel customer). Não faz login: o front chama createSession em seguida.</summary>
    [HttpPost("v1/accounts")]
    public async Task<IActionResult> Register(RegisterRequest body, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(body);
        var account = await register.HandleAsync(new RegisterCommand(body.Email ?? "", body.Password ?? "", body.Name ?? ""), ct);
        return Created("/v1/me", account);
    }

    /// <summary>getMe: a conta dona do access token.</summary>
    [HttpGet("v1/me")]
    [Authorize]
    public async Task<AccountView> Me(CancellationToken ct) =>
        await queries.GetAsync(Guid.Parse(User.Identity!.Name!), ct);
}

public sealed record RegisterRequest(string? Email, string? Password, string? Name);
