using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.History.domain.model.entity.activityHistory;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;
using Sydowwe.Framework.infrastructure.persistence.converter;

namespace AdhdTimeOrganizer.History.infrastructure.persistence.configuration.activityHistory;

public class ActivityHistoryConfiguration : IEntityTypeConfiguration<ActivityHistory>
{
    public void Configure(EntityTypeBuilder<ActivityHistory> builder)
    {
        builder.BaseEntityConfigure();

        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.Property(a => a.StartTimestamp).IsRequired();
        builder.Property(a => a.EndTimestamp).IsRequired();
        builder.Property(h => h.Length)
            .HasConversion(new IntTimeConverter()).IsRequired();

        builder.HasIndex(a => new { a.UserId, a.ActivityId, a.StartTimestamp }).IsUnique();

        // The unique index above cannot serve a user + date-range scan: ActivityId sits between the
        // two columns such a scan filters on, so Postgres can only use the UserId prefix and then
        // filters the rest. History dashboards and mv_activity_history_pattern both read that shape.
        builder.HasIndex(a => new { a.UserId, a.StartTimestamp });

        // Both FK columns are declared host-side (AppDbContext.ConfigureCrossSliceRelationships) —
        // this slice cannot see the principal types. The indexes belong here regardless: Postgres does
        // not index a FK for you, and SetNull has to find these rows on every to-do item delete. The
        // daily recap reads through the first one too (item ids IN (…), then user + day).
        builder.HasIndex(a => a.TodoListItemId);
        builder.HasIndex(a => a.RoutineTodoListId);
    }
}