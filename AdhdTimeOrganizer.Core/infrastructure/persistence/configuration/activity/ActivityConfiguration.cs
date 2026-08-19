using AdhdTimeOrganizer.Core.domain.model.entity.activity;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.activity;

public class ActivityConfiguration : IEntityTypeConfiguration<Activity>
{
    public void Configure(EntityTypeBuilder<Activity> builder)
    {
        builder.BaseNameTextEntityConfigure();

        builder.Property(x => x.IsUnavoidable).IsRequired();
        builder.Property(x => x.IsArchived).IsRequired().HasDefaultValue(false);

        builder.IsManyWithOneUser(u => u.ActivityList);
        builder.HasIndex(e => new { e.UserId, e.Name }).IsUnique();

        // The four pickers and the settings grid's default view all filter on this, and the grid pages
        // over it, so it leads the composite rather than trailing UserId alone.
        builder.HasIndex(e => new { e.UserId, e.IsArchived });

        builder.HasOne(e => e.Role)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        builder.HasOne(e => e.Category)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.Cascade);

        // Add indexes for foreign keys used in joins
        builder.HasIndex(e => e.RoleId);
        builder.HasIndex(e => e.CategoryId);

        // Composite index for user filtering with role/category lookups
        builder.HasIndex(e => new { e.UserId, e.RoleId });
        builder.HasIndex(e => new { e.UserId, e.CategoryId });
    }
}