using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.activityPlanning;

public class TimeAwayConfiguration : IEntityTypeConfiguration<TimeAway>
{
    public void Configure(EntityTypeBuilder<TimeAway> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser();

        builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
        builder.EnumColumn(t => t.Kind);
        builder.Property(t => t.StartDate).IsRequired();
        builder.Property(t => t.EndDate).IsRequired();
        builder.EnumColumn(t => t.Location);
        builder.Property(t => t.Notes).HasMaxLength(1000);

        // Deleting the template leaves the stretch, which only ever suggested it. The name is pinned at what EF
        // generated in the TimeAway migration, so a shift in configuration order cannot rename it.
        builder.HasOne(t => t.Template)
            .WithMany()
            .HasForeignKey(t => t.TemplateId)
            .IsRequired(false)
            .OnDelete(DeleteBehavior.SetNull)
            .HasConstraintName("fk_time_away_task_planner_day_templates_template_id");

        // The overlap check and the list both read a user's stretches by date. No unique index: stretches must not
        // overlap, which is a range rule the write endpoints enforce, not something an equality index can say.
        builder.HasIndex(t => new { t.UserId, t.StartDate });
    }
}
