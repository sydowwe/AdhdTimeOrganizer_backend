using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;

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
        builder.Property(x => x.IsFrozen).IsRequired();

        // The two travel together: a frozen row records when the freeze was spent, an unfrozen one has nothing
        // to record. Enforced here rather than trusted to the service, because a half-written pair reads as a
        // freeze that never happened in the ledger and nothing else would notice.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_routine_period_completion_frozen_at_matches_is_frozen",
            "(\"is_frozen\" AND \"frozen_at\" IS NOT NULL) OR (NOT \"is_frozen\" AND \"frozen_at\" IS NULL)"));

        builder.HasOne(x => x.RoutineTimePeriod)
            .WithMany(t => t.CompletionHistoryColl)
            .HasForeignKey(x => x.TimePeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TimePeriodId, x.PeriodStart }).IsUnique();
    }
}