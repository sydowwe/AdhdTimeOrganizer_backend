using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

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
        builder.Property(t => t.UsageCount).HasDefaultValue(0).IsRequired();
        builder.Property(t => t.SuggestedForDayType).IsRequired();

        builder.Property(t => t.Tags)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<List<string>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()
                )
            );

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