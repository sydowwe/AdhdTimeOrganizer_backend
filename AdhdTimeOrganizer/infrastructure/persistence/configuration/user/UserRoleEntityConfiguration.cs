using AdhdTimeOrganizer.domain.model.entity.user;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.user;

public class UserRoleEntityConfiguration : IEntityTypeConfiguration<UserRole>
{
    public void Configure(EntityTypeBuilder<UserRole> builder)
    {
        builder.ToTable("user_role");
        builder.Property(u => u.Description).HasMaxLength(50).IsRequired();
        builder.Property(u => u.IsDefault).IsRequired();
        builder.Property(u => u.RoleLevel).IsRequired();
        builder.Property(u => u.IsAssignable).IsRequired();
        builder.Property(u => u.CreatedDate).IsRequired();
    }
}