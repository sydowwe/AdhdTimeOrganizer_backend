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

        // SetNull, not Cascade — the same call as the two item links: rows are the source of truth for
        // recorded time, and a session is only a grouping over them. Nothing deletes a session that still has
        // rows today (the row delete removes it only once empty, the account delete cascades both), but if
        // anything ever does, the time survives as session-less rows, which is what every pre-session row
        // already is. Pinned name for the reason every cross-assembly-order FK here is pinned.
        builder.HasOne(a => a.Session)
            .WithMany(s => s.Records)
            .HasForeignKey(a => a.SessionId)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_activity_history_activity_history_session_session_id");

        // Also serves the row delete's "was that the session's last row" probe.
        builder.HasIndex(a => a.SessionId);

        builder.EnumColumn(a => a.Phase).HasMaxLength(20);
    }
}