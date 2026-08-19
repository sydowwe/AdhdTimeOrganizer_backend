using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.activity;

public class RoleConfiguration : IEntityTypeConfiguration<ActivityRole>
{
    public void Configure(EntityTypeBuilder<ActivityRole> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();

        builder.IsManyWithOneUser(u => u.RoleList);
        builder.HasIndex(r => new { r.UserId, r.Name }).IsUnique();

        // Stored as the C# member name, not the camelCase wire spelling — the column is readable in
        // psql and stays put if the wire contract ever changes.
        builder.Property(r => r.SystemKey)
            .HasConversion<string>()
            .HasMaxLength(30);

        // A user holds at most one role per system key, which is what makes
        // GetBySystemKeyActivityRoleEndpoint's FirstOrDefault deterministic. Filtered, because the
        // column is null on every user-created role and those must not collide with each other.
        builder.HasIndex(r => new { r.UserId, r.SystemKey })
            .IsUnique()
            .HasFilter("system_key IS NOT NULL");
    }
}