using AdhdTimeOrganizer.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.suggestion;

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
