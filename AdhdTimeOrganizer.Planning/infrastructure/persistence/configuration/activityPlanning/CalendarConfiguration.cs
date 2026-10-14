using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class CalendarConfiguration : IEntityTypeConfiguration<Calendar>
{
    public void Configure(EntityTypeBuilder<Calendar> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();
        builder.EnumColumn(c => c.DayType);

        builder.Property(c => c.Date).IsRequired();

        builder.EnumColumn(c => c.Location);

        builder.Property(c => c.HolidayKey).HasMaxLength(50);
        builder.Property(c => c.HolidayName).HasMaxLength(200);
        builder.Property(c => c.Label).HasMaxLength(100);
        builder.Property(c => c.AppliedTemplateName).HasMaxLength(200);
        builder.Property(c => c.Weather).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(1000);
        builder.Property(c => c.HasCustomSleepWindow).HasDefaultValue(false).IsRequired();

        // F4-02's link to the stretch of time away the day belongs to. SET NULL, so a stretch deleted outside its own
        // endpoint (the account going) never takes days with it; the endpoint un-stamps before that ever matters.
        // The constraint name is pinned for the reason PlannerTaskConfiguration pins its cross-slice one: EF derives
        // the name from configuration order, which shifts as slices move, and a rename is a DROP + ADD on this table.
        // A nullable column with no default is a metadata-only change in Postgres, even on calendar.
        builder.HasOne(c => c.TimeAway)
            .WithMany()
            .HasForeignKey(c => c.TimeAwayId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_calendar_time_away_time_away_id");
        builder.HasIndex(c => c.TimeAwayId);

        builder.HasIndex(c => new { c.UserId, c.Date }).IsUnique();
        // The one cross-user read by date: PlannerSlipNudgeJobHandler, every five minutes, wants the three dates around
        // UTC today for everyone. The unique index above leads with user_id and cannot serve it, and CalendarSeeder fills
        // whole years per user, so without this every tick scans every seeded day. Writes here are rare.
        builder.HasIndex(c => c.Date);
        // No index on DayType alone (PERF-5), for the same reason as planner_task.status: five distinct values
        // and no user prefix, so every calendar read reaches the row through (user_id, date) instead.
    }
}