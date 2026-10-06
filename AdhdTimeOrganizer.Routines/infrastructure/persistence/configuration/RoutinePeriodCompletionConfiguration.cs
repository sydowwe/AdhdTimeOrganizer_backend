using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;

/// <summary>
/// The one routines table that does <b>not</b> go through <c>BaseEntityConfigure()</c>, and the only one in the
/// module you have to hand-roll a key for. This is a deliberate, standing exception — not an oversight — so read
/// this before "fixing" it.
/// <para>
/// <c>BaseEntityConfigure&lt;TEntity&gt;</c> is constrained <c>where TEntity : class, IBaseTableEntity</c>, which
/// only <see cref="Sydowwe.Framework.domain.entity.@base.BaseTableEntity"/> satisfies;
/// <see cref="RoutinePeriodCompletion"/> derives from plain <c>BaseEntity</c> (it carries no <c>UserId</c> — it is
/// scoped transitively through its period), so the call would not compile. Promoting the entity to
/// <c>BaseTableEntity</c> to make it compile is a data migration, not a refactor: it renames the table, adds
/// <c>row_version</c>, and swaps <see cref="RoutinePeriodCompletion.CreatedAt"/> for the base's
/// <c>CreatedTimestamp</c>/<c>ModifiedTimestamp</c> pair. It was reviewed and declined; the ledger is append-only
/// and never concurrently updated, so the concurrency token it lacks buys nothing.
/// </para>
/// <para>
/// What it costs, written down so nobody rediscovers it from a schema diff:
/// <list type="bullet">
///   <item>the table is <c>routine_period_completions</c> — <b>plural</b>, because without
///   <c>BaseEntityConfigure</c> deriving it from the class name EF falls back to the DbSet name through
///   <c>UseSnakeCaseNamingConvention</c>. <c>ModuleSchemaTests</c> pins that plural, so it is load-bearing;</item>
///   <item>no <c>row_version</c> concurrency token — the only routines table without one;</item>
///   <item><c>Id</c> is an identity column, not <c>serial</c> like every other table here.</item>
/// </list>
/// A new convention added to <c>BaseEntityConfigure</c> will silently skip this table. Mirror it here by hand or
/// accept that it does not apply.
/// </para>
/// </summary>
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
        builder.Property(x => x.IsExcused).IsRequired();

        builder.ToTable(t =>
        {
            // The two travel together: a frozen row records when the freeze was spent, an unfrozen one has
            // nothing to record. Enforced here rather than trusted to the service, because a half-written pair
            // reads as a freeze that never happened in the ledger and nothing else would notice.
            t.HasCheckConstraint(
                "ck_routine_period_completion_frozen_at_matches_is_frozen",
                "(\"is_frozen\" AND \"frozen_at\" IS NOT NULL) OR (NOT \"is_frozen\" AND \"frozen_at\" IS NULL)");

            // An excused cycle is not a miss, so a freeze on it would be charged for nothing; a frozen one has
            // already been covered. Both set would be a budget spent twice over on one cycle.
            t.HasCheckConstraint(
                "ck_routine_period_completion_not_frozen_and_excused",
                "NOT (\"is_frozen\" AND \"is_excused\")");
        });

        builder.HasOne(x => x.RoutineTimePeriod)
            .WithMany(t => t.CompletionHistoryColl)
            .HasForeignKey(x => x.TimePeriodId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(x => new { x.TimePeriodId, x.PeriodStart }).IsUnique();
    }
}