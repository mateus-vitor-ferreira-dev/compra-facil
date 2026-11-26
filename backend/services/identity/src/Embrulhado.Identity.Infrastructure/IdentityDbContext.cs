using Embrulhado.BuildingBlocks.Messaging;
using Embrulhado.Identity.Domain;
using Embrulhado.Identity.Infrastructure.Keys;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Embrulhado.Identity.Infrastructure;

/// <summary>
/// Banco do identity (database-per-service): as tabelas do ASP.NET Core Identity com nomes curtos,
/// refresh tokens, tíquetes do segundo fator, chaves de assinatura, o anel de chaves do Data Protection e o outbox dos eventos de
/// conta.
/// </summary>
public sealed class IdentityDbContext(DbContextOptions<IdentityDbContext> options)
    : IdentityDbContext<User, Role, Guid>(options), IDataProtectionKeyContext
{
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<MfaTicket> MfaTickets => Set<MfaTicket>();
    public DbSet<GuestAccessLink> GuestAccessLinks => Set<GuestAccessLink>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.AddMessagingTables(); // outbox e inbox (BuildingBlocks)
        builder.ApplyConfigurationsFromAssembly(typeof(IdentityDbContext).Assembly);
    }
}
