using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.user;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace Sydowwe.Framework.infrastructure.persistence.configuration.user;

public class RefreshTokenCoreEntityConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        builder.BaseEntityConfigure();
        builder.Property(x => x.TokenHash).HasMaxLength(255);
        builder.Property(x => x.ReplacedByTokenHash).HasMaxLength(255);
        builder.Property(x => x.RevokedByIp).HasMaxLength(50);
        builder.Property(x => x.IpAddress).HasMaxLength(50);
        builder.Property(x => x.UserAgent).HasMaxLength(512);

        // The UserId FK is configured in BaseDbContext.OnModelCreating, where the concrete
        // user type (TUser) is known — the abstract BaseUser is excluded from the model.
    }
}