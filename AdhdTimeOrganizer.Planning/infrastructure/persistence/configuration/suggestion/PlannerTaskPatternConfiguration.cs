using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.suggestion;

public class PlannerTaskPatternConfiguration : IEntityTypeConfiguration<PlannerTaskPattern>
{
    public void Configure(EntityTypeBuilder<PlannerTaskPattern> builder)
    {
        builder.HasNoKey();
        builder.ToView("mv_planner_task_pattern");

        builder.HasOne(p => p.Activity)
            .WithMany()
            .HasForeignKey(p => p.ActivityId);

        builder.HasOne(p => p.Importance)
            .WithMany()
            .HasForeignKey(p => p.ImportanceId);
    }
}