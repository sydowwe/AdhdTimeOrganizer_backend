using AdhdTimeOrganizer.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class RoutineTimePeriodConfiguration : IEntityTypeConfiguration<RoutineTimePeriod>
{
    public void Configure(EntityTypeBuilder<RoutineTimePeriod> builder)
    {
        builder.BaseTextColorEntityConfigure();
        builder.Property(t=>t.LengthInDays).IsRequired();

        builder.HasMany(r => r.RoutineTodoListColl)
            .WithOne(t => t.RoutineTimePeriod)
            .HasForeignKey(t => t.TimePeriodId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => new { t.UserId, t.LengthInDays }).IsUnique();

    }
}