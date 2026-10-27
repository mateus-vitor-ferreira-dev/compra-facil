using System.Security.Cryptography;
using CompraFacil.Identity.Application.Tokens;
using CompraFacil.Identity.Domain;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace CompraFacil.Identity.Infrastructure.Keys;

/// <summary>
/// Chaves de assinatura no banco do identity, com a rotação do <see cref="SigningKeySchedule"/>. Cada
/// réplica relê as chaves a cada <see cref="CacheFor"/>; quem encontra a hora de criar a próxima o faz
/// sob um advisory lock do Postgres, então duas réplicas não criam duas chaves.
/// </summary>
internal sealed partial class DbSigningKeyRing(
    IServiceScopeFactory scopes,
    IDataProtectionProvider dataProtection,
    IOptions<JwtOptions> options,
    TimeProvider clock,
    ILogger<DbSigningKeyRing> logger) : ISigningKeyRing, IDisposable
{
    public const string ProtectorPurpose = "CompraFacil.Identity.SigningKeys.v1";
    public static readonly TimeSpan CacheFor = TimeSpan.FromMinutes(1);

    // chave do pg_advisory_xact_lock: qualquer número fixo, só o identity usa este banco
    private const long RotationLock = 0x1D_E7_17_00;
    private const int RsaKeySize = 2048;

    private readonly SemaphoreSlim _loading = new(1, 1);
    private readonly IDataProtector _protector = dataProtection.CreateProtector(ProtectorPurpose);
    private volatile Loaded? _loaded;

    private sealed record Loaded(IReadOnlyList<(SigningKeyLifetime Lifetime, RsaSecurityKey Private)> Keys, DateTimeOffset At);

    public async Task<SigningKeySet> CurrentAsync(CancellationToken ct)
    {
        var loaded = _loaded;
        if (loaded is null || clock.GetUtcNow() - loaded.At >= CacheFor)
        {
            loaded = await ReloadAsync(ct);
        }

        var now = clock.GetUtcNow();
        var active = SigningKeySchedule.Active(loaded.Keys.Select(k => k.Lifetime), now);
        if (active is null)
        {
            loaded = await ReloadAsync(ct, force: true); // venceu entre duas cargas: a recarga cria outra
            active = SigningKeySchedule.Active(loaded.Keys.Select(k => k.Lifetime), now)
                ?? throw new InvalidOperationException("nenhuma chave de assinatura ativa depois da rotação");
        }
        var signing = loaded.Keys.Single(k => k.Lifetime.KeyId == active.KeyId).Private;
        return new SigningKeySet(
            new SigningCredentials(signing, SecurityAlgorithms.RsaSha256),
            [.. loaded.Keys.Where(k => k.Lifetime.PublishedAt(now)).Select(k => PublicOf(k.Private))]);
    }

    public IReadOnlyList<SecurityKey> PublishedSnapshot()
    {
        var loaded = _loaded;
        if (loaded is null)
        {
            return [];
        }
        var now = clock.GetUtcNow();
        if (now - loaded.At >= CacheFor)
        {
            // réplica que só recebe GET /v1/me não passa pelo CurrentAsync: recarrega por fora, sem
            // segurar a requisição (a chave nova de outra réplica entra em até uma requisição)
            _ = Task.Run(RefreshInBackgroundAsync);
        }
        return [.. loaded.Keys.Where(k => k.Lifetime.PublishedAt(now)).Select(k => k.Private)];
    }

    private async Task RefreshInBackgroundAsync()
    {
        try
        {
            await ReloadAsync(CancellationToken.None);
        }
        catch (Exception e) when (e is not OutOfMemoryException)
        {
            LogBackgroundReloadFailed(logger, e);
        }
    }

    public void Dispose() => _loading.Dispose();

    private async Task<Loaded> ReloadAsync(CancellationToken ct, bool force = false)
    {
        await _loading.WaitAsync(ct);
        try
        {
            // outra requisição pode ter recarregado enquanto esta esperava
            if (!force && _loaded is { } fresh && clock.GetUtcNow() - fresh.At < CacheFor)
            {
                return fresh;
            }

            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
            var keys = await PublishedAsync(db, ct);
            var policy = options.Value.RotationPolicy;
            if (SigningKeySchedule.NextActivation([.. keys.Select(k => k.Lifetime)], clock.GetUtcNow(), policy) is not null)
            {
                keys = await RotateAsync(db, policy, ct);
            }

            // a chave já decifrada na carga anterior é reaproveitada: a recarga só lê as datas
            var known = _loaded?.Keys.ToDictionary(k => k.Lifetime.KeyId, k => k.Private) ?? [];
            var loaded = new Loaded(
                [.. keys.Select(k => (k.Lifetime, known.GetValueOrDefault(k.KeyId) ?? Unprotect(k)))], clock.GetUtcNow());
            _loaded = loaded;
            return loaded;
        }
        finally
        {
            _loading.Release();
        }
    }

    private async Task<List<SigningKey>> RotateAsync(IdentityDbContext db, KeyRotationPolicy policy, CancellationToken ct)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({RotationLock})", ct);

        // relê sob o lock: outra réplica pode ter acabado de criar a chave
        var keys = await PublishedAsync(db, ct);
        var now = clock.GetUtcNow();
        if (SigningKeySchedule.Resume([.. keys.Select(k => k.Lifetime)], now, policy) is { } resumed)
        {
            var latest = keys.Single(k => k.KeyId == resumed.KeyId);
            db.SigningKeys.Attach(latest);
            latest.Extend(resumed);
            LogKeyResumed(logger, latest.KeyId, latest.RetiresAt);
        }
        if (SigningKeySchedule.NextActivation([.. keys.Select(k => k.Lifetime)], now, policy) is { } activatesAt)
        {
            if (SigningKeySchedule.Active(keys.Select(k => k.Lifetime), now) is { } active
                && SigningKeySchedule.ExtendUntil(active, activatesAt, policy) is { } extended)
            {
                var current = keys.Single(k => k.KeyId == active.KeyId);
                db.SigningKeys.Attach(current);
                current.Extend(extended);
                LogKeyExtended(logger, current.KeyId, current.RetiresAt);
            }
            var key = Create(activatesAt, policy, now);
            db.SigningKeys.Add(key);
            keys.Add(key);
            LogKeyCreated(logger, key.KeyId, key.ActivatesAt, key.ExpiresAt);
        }
        // grava antes de limpar: a chave esticada tinha vencido e não pode sair junto
        await db.SaveChangesAsync(ct);
        // as que já saíram do JWKS não servem para nada
        await db.SigningKeys.Where(k => k.ExpiresAt <= now).ExecuteDeleteAsync(ct);
        keys.RemoveAll(k => k.ExpiresAt <= now);
        await tx.CommitAsync(ct);
        return keys;
    }

    // todas: as já vencidas saem do JWKS pelo PublishedAt e o RotateAsync as apaga
    private static Task<List<SigningKey>> PublishedAsync(IdentityDbContext db, CancellationToken ct) =>
        db.SigningKeys.AsNoTracking().OrderBy(k => k.ActivatesAt).ToListAsync(ct);

    private SigningKey Create(DateTimeOffset activatesAt, KeyRotationPolicy policy, DateTimeOffset now)
    {
        using var rsa = RSA.Create(RsaKeySize);
        var keyId = Base64UrlEncoder.Encode(
            JsonWebKeyConverter.ConvertFromRSASecurityKey(new RsaSecurityKey(rsa.ExportParameters(false))).ComputeJwkThumbprint());
        return new SigningKey(SigningKeySchedule.Plan(keyId, activatesAt, policy), _protector.Protect(rsa.ExportPkcs8PrivateKey()), now);
    }

    private RsaSecurityKey Unprotect(SigningKey key)
    {
        var rsa = RSA.Create(); // vive enquanto a chave estiver no cache (o GC finaliza depois)
        rsa.ImportPkcs8PrivateKey(_protector.Unprotect(key.ProtectedPrivateKey), out _);
        return new RsaSecurityKey(rsa) { KeyId = key.KeyId };
    }

    private static RsaSecurityKey PublicOf(RsaSecurityKey key) =>
        new(key.Rsa.ExportParameters(includePrivateParameters: false)) { KeyId = key.KeyId };

    [LoggerMessage(Level = LogLevel.Warning, Message = "falha ao recarregar as chaves de assinatura; segue com as da última carga")]
    private static partial void LogBackgroundReloadFailed(ILogger logger, Exception e);

    [LoggerMessage(Level = LogLevel.Warning, Message = "identity ficou fora além da folga: a chave {KeyId} volta a assinar até {RetiresAt}, quando a próxima (já publicada) assume")]
    private static partial void LogKeyResumed(ILogger logger, string keyId, DateTimeOffset retiresAt);

    [LoggerMessage(Level = LogLevel.Warning, Message = "rotação atrasada: a chave {KeyId} assina até {RetiresAt}, quando a próxima começa")]
    private static partial void LogKeyExtended(ILogger logger, string keyId, DateTimeOffset retiresAt);

    [LoggerMessage(Level = LogLevel.Information, Message = "chave de assinatura {KeyId} criada: assina a partir de {ActivatesAt}, sai do JWKS em {ExpiresAt}")]
    private static partial void LogKeyCreated(ILogger logger, string keyId, DateTimeOffset activatesAt, DateTimeOffset expiresAt);
}
