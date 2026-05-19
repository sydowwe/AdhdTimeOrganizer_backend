using AdhdTimeOrganizer.domain.model.entity.@base.core;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration;

public class BaseLookupConfiguration<T> : IEntityTypeConfiguration<T>
    where T : BaseLookup
{
    public void Configure(EntityTypeBuilder<T> e)
    {
        e.BaseEntityConfigure();
        e.IsManyWithOneUser();

        e.Property(x => x.Text).IsRequired().HasMaxLength(100);
        e.Property(x => x.SortOrder).IsRequired();

        e.HasIndex(x => new { x.UserId, x.Text }).IsUnique();
    }
}