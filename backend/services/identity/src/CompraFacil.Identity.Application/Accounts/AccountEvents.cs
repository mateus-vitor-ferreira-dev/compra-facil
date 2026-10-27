namespace CompraFacil.Identity.Application.Accounts;

/// <summary>Eventos publicados (contracts/asyncapi/identity.v1.yaml), todos pelo outbox.</summary>
public static class AccountEvents
{
    public const string Topic = "identity.account.v1";
    public const string AccountRegistered = nameof(AccountRegistered);
    public const string EmailConfirmationRequested = nameof(EmailConfirmationRequested);
}

/// <summary>Conta criada (o cadastro); os outros serviços ficam sabendo que o cliente existe.</summary>
public sealed record AccountRegisteredData(Guid AccountId, string Email, string Name, DateTimeOffset RegisteredAt);

/// <summary>
/// Pedido de confirmação de e-mail (no cadastro e no reenvio). O link leva o token, que só confirma
/// este e-mail desta conta e vence em <paramref name="ExpiresAt"/>; o notifications o manda por e-mail.
/// </summary>
public sealed record EmailConfirmationRequestedData(Guid AccountId, string Email, string Name, Uri ConfirmationUrl, DateTimeOffset ExpiresAt);
