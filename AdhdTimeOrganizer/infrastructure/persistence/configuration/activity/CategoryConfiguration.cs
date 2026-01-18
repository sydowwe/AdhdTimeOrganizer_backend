using AdhdTimeOrganizer.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity;

public class CategoryConfiguration : IEntityTypeConfiguration<ActivityCategory>
{
    public void Configure(EntityTypeBuilder<ActivityCategory> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();

        builder.IsManyWithOneUser(u=>u.CategoryList);
    }
}