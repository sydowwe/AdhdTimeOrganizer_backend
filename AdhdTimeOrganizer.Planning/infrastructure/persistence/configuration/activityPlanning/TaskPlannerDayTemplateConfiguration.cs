using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class TaskPlannerDayTemplateConfiguration : IEntityTypeConfiguration<TaskPlannerDayTemplate>
{
    public void Configure(EntityTypeBuilder<TaskPlannerDayTemplate> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(200);
        builder.Property(t => t.Description).HasMaxLength(1000);
        builder.Property(t => t.Icon).HasMaxLength(50);
        builder.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();
        builder.Property(t => t.IsPinned).HasDefaultValue(false).IsRequired();
        builder.Property(t => t.UsageCount).HasDefaultValue(0).IsRequired();
        builder.Property(t => t.SuggestedForDayType).IsRequired();

        // Tags are free text the user authors, so the comma-joined single column this used to be silently
        // split any tag containing a comma into two on the next read — and there is no validator rule on Tags
        // to stop one being written. Same shape and same reasoning as UserPlannerSettings.PredefinedSkipReasons
        // and Reminder.LeadOffsetsMinutes: a short primitive list nothing queries element-wise, so jsonb beats
        // both a delimiter and a child table, and it is lossless.
        // ScheduledDays below keeps the join: its elements are DayOfWeek names, which cannot contain a comma.
        builder.Property(t => t.Tags)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(t => t.ScheduledDays)
            .HasConversion(
                v => string.Join(',', v.Select(d => d.ToString())),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                    .Select(d => Enum.Parse<DayOfWeek>(d))
                    .ToList()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<List<DayOfWeek>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()
                )
            );

        builder.EnumColumn(t => t.SuggestedLocation);

        builder.HasIndex(t => new { t.UserId, t.Name });
        builder.HasIndex(t => t.SuggestedForDayType);
    }
}