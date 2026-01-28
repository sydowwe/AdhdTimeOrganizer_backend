using AdhdTimeOrganizer.domain.model.entity.timer;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.timer;

public class TimerPresetConfiguration : IEntityTypeConfiguration<TimerPreset>
{
    public void Configure(EntityTypeBuilder<TimerPreset> builder)
    {
        builder.BaseEntityConfigure();

        builder.Property(t => t.Duration)
            .IsRequired();

        builder.IsManyWithOneUser();

        builder.HasOne(t => t.Activity)
            .WithMany()
            .HasForeignKey(t => t.ActivityId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.UserId);
    }
}
