using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;
using User = AdhdTimeOrganizer.Core.domain.model.entity.user.User;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.user;

/// <summary>
/// Configures Framework's <see cref="RefreshToken"/> for this host. Framework ships its own
/// <c>RefreshTokenCoreEntityConfiguration</c>, but it lives in an assembly <c>AppDbContext</c> never
/// applies and deliberately leaves the FK unconfigured (it doesn't know the concrete user type) — so
/// this file stays, supplying the FK plus the lookup indexes and the column widths already in the
/// database.
/// </summary>
public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.BaseEntityConfigure();

        // Configured from the principal end: Framework's RefreshToken has a UserId but no User
        // navigation, so IsManyWithOneUser (which requires BaseEntityWithUser<TUser>) doesn't apply.
        // No standalone user_id index — ix_refresh_token_user_revoked below already leads with it.
        builder.HasOne<User>()
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(rt => rt.UserId)
            .IsRequired()
            .OnDelete(DeleteBehavior.Cascade);

        builder.Property(rt => rt.TokenHash)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(rt => rt.ExpiresAt)
            .IsRequired();

        builder.Property(rt => rt.IsRevoked)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(rt => rt.ReplacedByTokenHash)
            .HasMaxLength(64);

        builder.Property(rt => rt.IsExtensionClient)
            .IsRequired()
            .HasDefaultValue(false);

        builder.Property(rt => rt.RevokedByIp)
            .HasMaxLength(45);

        builder.HasIndex(rt => rt.TokenHash)
            .HasDatabaseName("ix_refresh_token_token_hash");

        builder.HasIndex(rt => new { rt.UserId, rt.IsRevoked })
            .HasDatabaseName("ix_refresh_token_user_revoked");

        builder.HasIndex(rt => rt.ExpiresAt)
            .HasDatabaseName("ix_refresh_token_expires_at");

        builder.Property(rt => rt.UserAgent).HasMaxLength(512);
        builder.Property(rt => rt.IpAddress).HasMaxLength(45);
    }
}