using AdhdTimeOrganizer.Planning.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.Planning.infrastructure.persistence.configuration.suggestion;

public class PlannerSuggestionFromDayTemplateConfiguration : IEntityTypeConfiguration<PlannerSuggestionFromDayTemplate>
{
    public void Configure(EntityTypeBuilder<PlannerSuggestionFromDayTemplate> builder)
    {
        builder.HasNoKey();
        builder.ToView("mv_template_suggestion_pattern");

        builder.HasOne(p => p.Template)
            .WithMany()
            .HasForeignKey(p => p.TemplateId);
    }
}