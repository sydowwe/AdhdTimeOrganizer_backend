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

        // Cascade is unreachable in practice, not intent: RoleId is NOT NULL so SetNull is unavailable,
        // and DeleteActivityRoleEndpoint refuses (409) any role that still has activities under it — an
        // activity cascade here would take the whole activity's cross-slice tail with it (history rows,
        // planner tasks, to-do items, profiles, presets, tracker mappings). Restrict at the database level
        // would be the belt to that braces, but it turns the endpoint's own delete into an opaque 409 from
        // Postgres for anything the count misses, so the refusal is kept where it can explain itself.
        builder.HasOne(e => e.Role)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.RoleId)
            .OnDelete(DeleteBehavior.Cascade)
            .IsRequired();

        // SetNull, not Cascade: CategoryId is optional, so losing a category means losing a
        // classification, not the thing classified. Same shape as TodoListItem.PairedLeisureActivityId.
        builder.HasOne(e => e.Category)
            .WithMany(r => r.Activities)
            .HasForeignKey(e => e.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);

        // Add indexes for foreign keys used in joins
        builder.HasIndex(e => e.RoleId);
        builder.HasIndex(e => e.CategoryId);

        // Composite index for user filtering with role/category lookups
        builder.HasIndex(e => new { e.UserId, e.RoleId });
        builder.HasIndex(e => new { e.UserId, e.CategoryId });
    }
}