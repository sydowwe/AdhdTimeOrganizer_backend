using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.suggestion;

public class PlannerSuggestionDismissalConfiguration : IEntityTypeConfiguration<PlannerSuggestionDismissal>
{
    public void Configure(EntityTypeBuilder<PlannerSuggestionDismissal> builder)
    {
        builder.BaseEntityConfigure();

        // Parameterless, like LeisureSuggestionRecord's: no inverse collection on User or Activity, which would
        // make Core reference this slice. Both cascade — a dismissal of a deleted activity's pattern hides nothing.
        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.EnumColumn(d => d.PatternType);
        builder.Property(d => d.PatternValue).IsRequired();
        builder.Property(d => d.DismissedOn).IsRequired();

        // The natural key, and the upsert target of POST /repeating-planner-task/suggestions/dismiss. Leads on
        // UserId, which is what the suggestions read filters on.
        builder.HasIndex(d => new { d.UserId, d.ActivityId, d.PatternType, d.PatternValue }).IsUnique();
    }
}
