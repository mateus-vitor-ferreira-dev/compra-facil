namespace CompraFacil.Identity.Domain;

/// <summary>Para que serve o tíquete do segundo fator.</summary>
public enum MfaTicketPurpose
{
    /// <summary>A senha (ou o Google) conferiu e a conta tem 2FA: falta o código do autenticador.</summary>
    Login,

    /// <summary>Operador sem 2FA: o tíquete só serve para cadastrar o autenticador, nunca abre sessão sozinho.</summary>
    Setup,
}

/// <summary>
/// O que o primeiro fator (senha ou Google) devolve quando a conta precisa do segundo: um valor aleatório que
/// não é sessão nem token de API. Vale poucos minutos, uma vez só (o primeiro código certo o gasta), e só
/// com o carimbo de segurança que a conta tinha quando ele saiu: trocar a senha no meio o derruba. O banco
/// guarda só o SHA-256 do valor.
/// </summary>
public sealed class MfaTicket
{
    /// <summary>Do login: o tempo de abrir o autenticador e digitar o código.</summary>
    public static readonly TimeSpan LoginLifetime = TimeSpan.FromMinutes(5);

    /// <summary>Do cadastro do autenticador: instalar o app e ler o QR code leva mais tempo.</summary>
    public static readonly TimeSpan SetupLifetime = TimeSpan.FromMinutes(15);

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public MfaTicketPurpose Purpose { get; private set; }
    public byte[] TokenHash { get; private set; } = [];
    public string SecurityStamp { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    /// <summary>Quando o código certo o gastou; daí em diante, ele não vale mais.</summary>
    public DateTimeOffset? ConsumedAt { get; private set; }

    private MfaTicket()
    {
    }

    public static MfaTicket Issue(Guid userId, MfaTicketPurpose purpose, string? securityStamp, byte[] tokenHash, DateTimeOffset now) => new()
    {
        Id = Guid.CreateVersion7(),
        UserId = userId,
        Purpose = purpose,
        SecurityStamp = securityStamp ?? "",
        TokenHash = tokenHash,
        CreatedAt = now,
        ExpiresAt = now + LifetimeOf(purpose),
    };

    public static TimeSpan LifetimeOf(MfaTicketPurpose purpose) => purpose == MfaTicketPurpose.Setup ? SetupLifetime : LoginLifetime;

    /// <summary>Ainda serve para <paramref name="purpose"/>: não venceu, não foi gasto e a senha não mudou.</summary>
    public bool IsUsable(MfaTicketPurpose purpose, string? currentStamp, DateTimeOffset now) =>
        Purpose == purpose && ConsumedAt is null && now < ExpiresAt
        && string.Equals(SecurityStamp, currentStamp ?? "", StringComparison.Ordinal);
}
