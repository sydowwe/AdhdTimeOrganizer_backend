using AdhdTimeOrganizer.domain.model.entity.user;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser(u => u.RefreshTokens);

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
    }
}
