using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity;

public class CategoryConfiguration : IEntityTypeConfiguration<ActivityCategory>
{
    public void Configure(EntityTypeBuilder<ActivityCategory> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();

        builder.IsManyWithOneUser(u => u.CategoryList);
        builder.HasIndex(r => new { r.UserId, r.Name }).IsUnique();
    }
}