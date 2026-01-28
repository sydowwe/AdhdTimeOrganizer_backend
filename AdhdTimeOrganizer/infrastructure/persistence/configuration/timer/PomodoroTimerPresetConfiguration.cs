using AdhdTimeOrganizer.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.timer;

public class PomodoroTimerPresetConfiguration : IEntityTypeConfiguration<PomodoroTimerPreset>
{
    public void Configure(EntityTypeBuilder<PomodoroTimerPreset> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(t => t.Name)
            .IsRequired()
            .HasMaxLength(255);

        builder.Property(t => t.FocusDuration)
            .IsRequired();

        builder.Property(t => t.ShortBreakDuration)
            .IsRequired();

        builder.Property(t => t.LongBreakDuration)
            .IsRequired();

        builder.Property(t => t.FocusPeriodInCycleCount)
            .IsRequired();

        builder.Property(t => t.NumberOfCycles)
            .IsRequired();

        builder.IsManyWithOneUser();

        builder.HasOne(t => t.FocusActivity)
            .WithMany()
            .HasForeignKey(t => t.FocusActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(t => t.RestActivity)
            .WithMany()
            .HasForeignKey(t => t.RestActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.UserId);
    }
}
