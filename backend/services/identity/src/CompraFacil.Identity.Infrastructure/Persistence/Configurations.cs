using CompraFacil.Identity.Domain;
using CompraFacil.Identity.Infrastructure.Keys;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CompraFacil.Identity.Infrastructure.Persistence;

// tabelas do Identity sem o prefixo AspNet (o snake_case viraria asp_net_users)
internal sealed class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("users");
        builder.Property(u => u.Name).HasMaxLength(User.MaxNameLength);
        builder.Property(u => u.Email).HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.NormalizedEmail).HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.UserName).HasMaxLength(User.MaxEmailLength);
        builder.Property(u => u.NormalizedUserName).HasMaxLength(User.MaxEmailLength);
        // RequireUniqueEmail já confere na aplicação; o índice único segura a corrida entre dois cadastros
        builder.HasIndex(u => u.NormalizedEmail).IsUnique();
    }
}

internal sealed class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("roles");
        builder.HasData(
            new Role { Id = Roles.CustomerId, Name = Roles.Customer, NormalizedName = "CUSTOMER", ConcurrencyStamp = "customer" },
            new Role { Id = Roles.AdminId, Name = Roles.Admin, NormalizedName = "ADMIN", ConcurrencyStamp = "admin" });
    }
}

internal sealed class IdentityTablesConfiguration :
    IEntityTypeConfiguration<IdentityUserRole<Guid>>,
    IEntityTypeConfiguration<IdentityUserClaim<Guid>>,
    IEntityTypeConfiguration<IdentityUserLogin<Guid>>,
    IEntityTypeConfiguration<IdentityUserToken<Guid>>,
    IEntityTypeConfiguration<IdentityRoleClaim<Guid>>
{
    public void Configure(EntityTypeBuilder<IdentityUserRole<Guid>> builder) => builder.ToTable("user_roles");
    public void Configure(EntityTypeBuilder<IdentityUserClaim<Guid>> builder) => builder.ToTable("user_claims");
    public void Configure(EntityTypeBuilder<IdentityUserLogin<Guid>> builder) => builder.ToTable("user_logins");
    public void Configure(EntityTypeBuilder<IdentityUserToken<Guid>> builder) => builder.ToTable("user_tokens");
    public void Configure(EntityTypeBuilder<IdentityRoleClaim<Guid>> builder) => builder.ToTable("role_claims");
}

internal sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.ToTable("refresh_tokens");
        builder.HasKey(t => t.Id);
        builder.Property(t => t.TokenHash).HasMaxLength(32);
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.FamilyId);
        builder.Property(t => t.RevocationReason).HasConversion<string>().HasMaxLength(16);
        builder.Property(t => t.Version).IsRowVersion(); // xmin
        builder.HasOne<User>().WithMany().HasForeignKey(t => t.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}

internal sealed class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.ToTable("signing_keys");
        builder.HasKey(k => k.KeyId);
        builder.Property(k => k.KeyId).HasMaxLength(64);
    }
}
