using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace Sydowwe.Framework.infrastructure.persistence.configuration;

public class BaseLookupConfiguration<T> : IEntityTypeConfiguration<T>
    where T : BaseLookup
{
    public void Configure(EntityTypeBuilder<T> e)
    {
        e.BaseEntityConfigure();

        e.Property(x => x.Text).IsRequired().HasMaxLength(100);
        e.Property(x => x.SortOrder).IsRequired();

        e.HasIndex(x => x.Text).IsUnique();
    }
}
