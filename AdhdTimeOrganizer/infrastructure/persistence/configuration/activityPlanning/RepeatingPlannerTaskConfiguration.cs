using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class RepeatingPlannerTaskConfiguration : IEntityTypeConfiguration<RepeatingPlannerTask>
{
    public void Configure(EntityTypeBuilder<RepeatingPlannerTask> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.Property(t => t.StartTime).IsRequired();
        builder.Property(t => t.EndTime).IsRequired();
        builder.Property(t => t.IsBackground).IsRequired();
        builder.Property(t => t.IsActive).HasDefaultValue(true).IsRequired();

        builder.Property(t => t.Location).HasMaxLength(200);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        builder.EnumColumn(t => t.RecurrenceType);

        builder.Property(t => t.ActiveFromDate);
        builder.Property(t => t.ActiveToDate);

        builder.Property(t => t.ScheduledDays)
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

        builder.Property(t => t.ScheduledDates)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries)
                       .Select(int.Parse)
                       .ToList()
            )
            .Metadata.SetValueComparer(
                new ValueComparer<List<int>>(
                    (c1, c2) => c1!.SequenceEqual(c2!),
                    c => c.Aggregate(0, (a, v) => HashCode.Combine(a, v.GetHashCode())),
                    c => c.ToList()
                )
            );

        builder.Property(t => t.ScheduledForDayTypes)
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

        builder.HasOne(t => t.Importance)
            .WithMany()
            .HasForeignKey(t => t.ImportanceId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(t => new { t.UserId, t.IsActive });
        builder.HasIndex(t => t.RecurrenceType);
    }
}
