using AdhdTimeOrganizer.Command.domain.model.entity.activity;
using AdhdTimeOrganizer.Common.infrastructure.persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Command.infrastructure.persistence.configuration;

public class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.BaseNameTextColorEntityConfigure();

        builder.IsManyWithOneUser(u=>u.CategoryList);
    }
}