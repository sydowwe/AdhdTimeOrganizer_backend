using AdhdTimeOrganizer.domain.model.entity.activity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activity;

public class RoleConfiguration : IEntityTypeConfiguration<ActivityRole>
{
    public void Configure(EntityTypeBuilder<ActivityRole> builder)
    {
        builder.BaseNameTextColorEntityConfigure();

        builder.IsManyWithOneUser(u=>u.RoleList);
    }
}