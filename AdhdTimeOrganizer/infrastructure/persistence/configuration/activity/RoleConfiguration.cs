using AdhdTimeOrganizer.domain.model.entity.activity;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity;

public class RoleConfiguration : IEntityTypeConfiguration<ActivityRole>
{
    public void Configure(EntityTypeBuilder<ActivityRole> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();

        builder.IsManyWithOneUser(u=>u.RoleList);
    }
}