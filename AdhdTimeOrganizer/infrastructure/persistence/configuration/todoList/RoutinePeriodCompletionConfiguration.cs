using AdhdTimeOrganizer.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class RoutinePeriodCompletionConfiguration : IEntityTypeConfiguration<RoutinePeriodCompletion>
{
    public void Configure(EntityTypeBuilder<RoutinePeriodCompletion> builder)
    {
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TimePeriodId).IsRequired();
        builder.Property(x => x.PeriodStart).IsRequired();
        builder.Property(x => x.PeriodEnd).IsRequired();
        builder.Property(x => x.CompletedCount).IsRequired();
        builder.Property(x => x.TotalCount).IsRequired();
        builder.Property(x => x.CreatedAt).IsRequired();

        builder.HasOne(x => x.RoutineTimePeriod)
            .WithMany(t => t.CompletionHistoryColl)
            .HasForeignKey(x => x.TimePeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TimePeriodId, x.PeriodStart }).IsUnique();
    }
}
