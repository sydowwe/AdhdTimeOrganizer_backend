using AdhdTimeOrganizer.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration;

public class SelectOptionBaseConfiguration<T> : IEntityTypeConfiguration<T>
    where T : SelectOptionBase
{
    public void Configure(EntityTypeBuilder<T> e)
    {
        e.BaseEntityConfigure();

        e.Property(x => x.Text).IsRequired().HasMaxLength(100);
        e.Property(x => x.SortOrder).IsRequired();

        e.HasIndex(x => x.Text).IsUnique();
    }
}