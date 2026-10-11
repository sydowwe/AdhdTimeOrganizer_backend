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

        builder.HasIndex(c => new { c.UserId, c.Date }).IsUnique();
        // The one cross-user read by date: PlannerSlipNudgeJobHandler, every five minutes, wants the three dates around
        // UTC today for everyone. The unique index above leads with user_id and cannot serve it, and CalendarSeeder fills
        // whole years per user, so without this every tick scans every seeded day. Writes here are rare.
        builder.HasIndex(c => c.Date);
        // No index on DayType alone (PERF-5), for the same reason as planner_task.status: five distinct values
        // and no user prefix, so every calendar read reaches the row through (user_id, date) instead.
    }
}