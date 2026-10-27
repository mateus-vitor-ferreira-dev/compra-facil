namespace CompraFacil.Identity.Domain;

/// <summary>
/// Quando trocar a chave que assina os JWTs.
/// </summary>
/// <param name="RotateEvery">Quanto tempo cada chave assina.</param>
/// <param name="PublishAhead">
/// A próxima chave entra no JWKS esse tempo antes de começar a assinar: quem guarda o JWKS em cache
/// (os outros serviços) já a conhece quando o primeiro token com ela chega. Maior que o cache deles.
/// </param>
/// <param name="TokenLifetime">Validade do access token: a chave fica no JWKS até o último token dela vencer.</param>
public sealed record KeyRotationPolicy(TimeSpan RotateEvery, TimeSpan PublishAhead, TimeSpan TokenLifetime)
{
    public static readonly KeyRotationPolicy Default = new(TimeSpan.FromDays(30), TimeSpan.FromDays(1), TimeSpan.FromMinutes(15));

    public void Validate()
    {
        if (RotateEvery <= TimeSpan.Zero || PublishAhead <= TimeSpan.Zero || TokenLifetime <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("RotateEvery, PublishAhead e TokenLifetime precisam ser positivos");
        }
        if (PublishAhead >= RotateEvery)
        {
            throw new InvalidOperationException("PublishAhead precisa ser menor que RotateEvery");
        }
    }
}

/// <summary>A vida de uma chave: publicada desde a criação, assina em [ActivatesAt, RetiresAt), sai do JWKS em ExpiresAt.</summary>
public sealed record SigningKeyLifetime(string KeyId, DateTimeOffset ActivatesAt, DateTimeOffset RetiresAt, DateTimeOffset ExpiresAt)
{
    public bool SignsAt(DateTimeOffset now) => ActivatesAt <= now && now < RetiresAt;

    public bool PublishedAt(DateTimeOffset now) => now < ExpiresAt;
}

/// <summary>
/// Rotação das chaves de assinatura, sem relógio nem banco (testável). Em dia, cada chave assina por
/// <c>RotateEvery</c>, a sucessora é publicada <c>PublishAhead</c> antes e a antiga sai do JWKS quando
/// os tokens dela vencem. Se o serviço perdeu a janela de troca, a atual é esticada (<see cref="ExtendUntil"/>);
/// se ficou fora tanto que ela venceu, a mais recente volta a assinar (<see cref="Resume"/>). Nos dois
/// casos a sucessora sai publicada antes de assinar: chave sem aviso, só na primeira subida.
/// </summary>
public static class SigningKeySchedule
{
    /// <summary>A chave que assina agora: a mais recente já ativa. Nula se nenhuma vale.</summary>
    public static SigningKeyLifetime? Active(IEnumerable<SigningKeyLifetime> keys, DateTimeOffset now) =>
        keys.Where(k => k.SignsAt(now)).MaxBy(k => k.ActivatesAt);

    /// <summary>
    /// Depois de o serviço ficar fora além da folga, nenhuma chave assina mais. Em vez de criar uma que
    /// assine já (que os serviços ainda não conhecem), a mais recente volta a assinar por mais
    /// <c>PublishAhead</c>, tempo de a sucessora ser publicada antes de assumir. Nulo se alguma chave
    /// ainda assina ou se não há chave nenhuma (primeira subida: não há token para validar ainda).
    /// </summary>
    public static SigningKeyLifetime? Resume(IReadOnlyCollection<SigningKeyLifetime> keys, DateTimeOffset now, KeyRotationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        if (Active(keys, now) is not null || keys.MaxBy(k => k.ActivatesAt) is not { } latest || latest.ActivatesAt > now)
        {
            return null;
        }
        var retiresAt = now + policy.PublishAhead;
        return latest with { RetiresAt = retiresAt, ExpiresAt = retiresAt + policy.TokenLifetime };
    }

    /// <summary>Quando a próxima chave deve começar a assinar, se for hora de criá-la; senão, nulo.</summary>
    public static DateTimeOffset? NextActivation(IReadOnlyCollection<SigningKeyLifetime> keys, DateTimeOffset now, KeyRotationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        var active = Active(keys, now);
        if (active is null)
        {
            return now; // primeira subida (ou, sem o Resume antes, a chave venceu sem sucessora)
        }
        if (keys.Any(k => k.ActivatesAt > now))
        {
            return null; // a sucessora já está publicada
        }
        var due = active.ActivatesAt + policy.RotateEvery;
        if (now < due - policy.PublishAhead)
        {
            return null;
        }
        var earliest = now + policy.PublishAhead;
        return due > earliest ? due : earliest;
    }

    /// <summary>
    /// A chave que assina agora, esticada até a sucessora começar: se a rotação atrasou (o serviço passou da
    /// janela de troca), a sucessora só assina <c>PublishAhead</c> depois de publicada, e a atual não pode
    /// parar antes disso. Nulo se a atual já cobre o intervalo.
    /// </summary>
    public static SigningKeyLifetime? ExtendUntil(SigningKeyLifetime active, DateTimeOffset successorActivatesAt, KeyRotationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(active);
        ArgumentNullException.ThrowIfNull(policy);
        return active.RetiresAt >= successorActivatesAt
            ? null
            : active with { RetiresAt = successorActivatesAt, ExpiresAt = successorActivatesAt + policy.TokenLifetime };
    }

    /// <summary>A vida de uma chave nova que começa a assinar em <paramref name="activatesAt"/>.</summary>
    public static SigningKeyLifetime Plan(string keyId, DateTimeOffset activatesAt, KeyRotationPolicy policy)
    {
        ArgumentNullException.ThrowIfNull(policy);
        // folga de PublishAhead: se a sucessora atrasar até esse tanto, esta ainda assina
        var retiresAt = activatesAt + policy.RotateEvery + policy.PublishAhead;
        return new SigningKeyLifetime(keyId, activatesAt, retiresAt, retiresAt + policy.TokenLifetime);
    }
}
