using CompraFacil.BuildingBlocks.Messaging;
using CompraFacil.BuildingBlocks.Web;
using CompraFacil.Identity.Domain;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace CompraFacil.Identity.Application.Accounts;

/// <summary>Compra sem conta (seção Identity:GuestAccess).</summary>
public sealed class GuestAccessOptions
{
    public const string Section = "Identity:GuestAccess";

    /// <summary>Rota do front que abre a sessão; recebe <c>{token}</c>.</summary>
    public string UrlTemplate { get; set; } = "http://localhost:3000/acompanhar/abrir?token={token}";

    /// <summary>Validade do link: cobre a entrega, os 7 dias para desistir e a garantia legal.</summary>
    public TimeSpan LinkLifetime { get; set; } = TimeSpan.FromDays(180);

    /// <summary>Intervalo mínimo entre dois links da mesma compra: o reenvio não vira spam contra quem é dono do e-mail.</summary>
    public TimeSpan ResendInterval { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Quantas compras recentes do mesmo e-mail recebem o link num reenvio.</summary>
    public int MaxPurchasesPerResend { get; set; } = 5;
}

public sealed record StartGuestCommand(string Email, string Name, Guid CheckoutKey);

public sealed class StartGuestValidator : AbstractValidator<StartGuestCommand>
{
    public StartGuestValidator()
    {
        RuleFor(c => c.Email).NotEmpty().EmailAddress().MaximumLength(User.MaxEmailLength);
        RuleFor(c => c.Name).NotEmpty().MaximumLength(User.MaxNameLength);
        RuleFor(c => c.CheckoutKey).NotEmpty();
    }
}

/// <summary>O que a compra sem conta precisa guardar e procurar.</summary>
public interface IGuestRepository
{
    Task<User?> FindByCheckoutKeyAsync(Guid checkoutKey, CancellationToken ct);

    Task<IReadOnlyList<User>> RecentByEmailAsync(string normalizedEmail, int take, CancellationToken ct);

    void AddLink(GuestAccessLink link);

    Task<GuestAccessLink?> FindLinkAsync(byte[] tokenHash, CancellationToken ct);

    Task<DateTimeOffset?> LastLinkAtAsync(Guid userId, CancellationToken ct);
}

/// <summary>
/// Comprar sem cadastro. Cada compra ganha uma conta própria e sem senha, com o papel <c>customer</c>, e o
/// pedido, a desistência e a nota funcionam como para qualquer cliente. Digitar um e-mail nunca dá acesso a
/// nada que já exista com ele: só a chave do mesmo formulário ou o link recebido naquele e-mail reabrem a compra.
/// </summary>
public sealed class GuestCheckout(
    IValidator<StartGuestCommand> validator,
    UserManager<User> users,
    IGuestRepository guests,
    Sessions sessions,
    IOutbox outbox,
    IAccountTransaction transaction,
    IOptions<GuestAccessOptions> options,
    TimeProvider clock)
{
    private const string InvalidLink = "link de acesso inválido ou vencido";

    public async Task<IssuedSession> StartAsync(StartGuestCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);
        await validator.ValidateAndThrowAsync(command, ct);

        var normalized = users.NormalizeEmail(command.Email.Trim());
        var existing = await guests.FindByCheckoutKeyAsync(command.CheckoutKey, ct);
        if (existing is not null)
        {
            // o mesmo formulário de novo (clique duplo, nova tentativa): a mesma compra, e um pedido só
            return existing.NormalizedGuestEmail == normalized
                ? await sessions.StartAsync(existing, ct)
                : throw new ConflictException("Compra já iniciada", "este formulário já foi enviado com outro e-mail");
        }

        var user = User.CreateGuest(command.Email, normalized, command.Name, command.CheckoutKey, clock.GetUtcNow());
        await transaction.RunAsync(async () =>
        {
            Check(await users.CreateAsync(user));
            Check(await users.AddToRoleAsync(user, Roles.Customer));
            Enqueue(user);
            return user;
        }, ct);
        return await sessions.StartAsync(user, ct);
    }

    /// <summary>Abre a sessão pelo link do e-mail. Link desconhecido e vencido dão o mesmo erro.</summary>
    public async Task<IssuedSession> AccessAsync(string token, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > RefreshTokenValue.MaxLength)
        {
            throw new UnauthorizedException(InvalidLink);
        }
        var link = await guests.FindLinkAsync(RefreshTokenValue.Hash(token), ct);
        var user = link is not null && link.IsActive(clock.GetUtcNow())
            ? await users.FindByIdAsync(link.UserId.ToString())
            : null;
        return user is { IsGuest: true }
            ? await sessions.StartAsync(user, ct)
            : throw new UnauthorizedException(InvalidLink);
    }

    /// <summary>Manda o link de novo para as compras recentes do e-mail. Quem chama não fica sabendo se há alguma.</summary>
    public async Task ResendAsync(string email, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(email) || email.Length > User.MaxEmailLength)
        {
            return;
        }
        var recent = await guests.RecentByEmailAsync(
            users.NormalizeEmail(email.Trim()), options.Value.MaxPurchasesPerResend, ct);
        var now = clock.GetUtcNow();
        foreach (var user in recent)
        {
            if (await guests.LastLinkAtAsync(user.Id, ct) is { } last && now - last < options.Value.ResendInterval)
            {
                continue;
            }
            await transaction.RunAsync(() =>
            {
                Enqueue(user);
                return Task.FromResult(true);
            }, ct);
        }
    }

    private void Enqueue(User user)
    {
        var token = RefreshTokenValue.New();
        var now = clock.GetUtcNow();
        guests.AddLink(GuestAccessLink.Issue(user.Id, RefreshTokenValue.Hash(token), now, options.Value.LinkLifetime));
        var url = new Uri(options.Value.UrlTemplate.Replace("{token}", token, StringComparison.Ordinal));
        outbox.Add(AccountEvents.Topic, user.Id.ToString(), AccountEvents.GuestAccessRequested,
            new GuestAccessRequestedData(user.Id, user.ContactEmail, user.Name, url, now + options.Value.LinkLifetime));
    }

    private static void Check(IdentityResult result)
    {
        if (!result.Succeeded)
        {
            throw new ConflictException("Compra não iniciada", string.Join("; ", result.Errors.Select(e => e.Description)));
        }
    }
}
