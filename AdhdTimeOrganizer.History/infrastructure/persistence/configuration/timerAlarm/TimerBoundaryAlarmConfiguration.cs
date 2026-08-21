using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.History.domain.model.entity.timerAlarm;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.History.infrastructure.persistence.configuration.timerAlarm;

public class TimerBoundaryAlarmConfiguration : IEntityTypeConfiguration<TimerBoundaryAlarm>
{
    public void Configure(EntityTypeBuilder<TimerBoundaryAlarm> builder)
    {
        builder.BaseEntityConfigure();

        // Cascade from the user, unlike ActivityHistory's SetNull links: an alarm is scheduling state, not
        // recorded time, so a deleted account's pending alarms should go with it rather than survive as
        // orphans that would fire at nobody.
        builder.IsManyWithOneUser();

        builder.Property(a => a.SessionId).IsRequired();
        builder.Property(a => a.BoundaryAt).IsRequired();
        builder.Property(a => a.Phase).IsRequired();
        builder.Property(a => a.Url).HasMaxLength(512);

        // Cancel-by-session is the hot path (Stop cancels the whole cycle), and it is always user-scoped.
        builder.HasIndex(a => new { a.UserId, a.SessionId });

        // The boot registrar's only query: every pending alarm across all users, split on BoundaryAt into
        // "re-register" and "too stale, delete". It runs unscoped, hence UserId is not the leading column.
        builder.HasIndex(a => a.BoundaryAt);

        // ActivityId is deliberately a plain column — no FK, no navigation. See the entity's remarks: these
        // rows live for minutes, and an orphan degrades to a name-less notification body rather than
        // needing a constraint in the Activity FK family. No index either: it is only ever read by row id.
    }
}
