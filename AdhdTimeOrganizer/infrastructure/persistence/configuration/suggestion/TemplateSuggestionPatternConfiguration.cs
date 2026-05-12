using AdhdTimeOrganizer.domain.model.entity.suggestion;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.suggestion;

public class TemplateSuggestionPatternConfiguration : IEntityTypeConfiguration<TemplateSuggestionPattern>
{
    public void Configure(EntityTypeBuilder<TemplateSuggestionPattern> builder)
    {
        builder.HasNoKey();
        builder.ToView("mv_template_suggestion_pattern");

        builder.HasOne(p => p.Template)
            .WithMany()
            .HasForeignKey(p => p.TemplateId);
    }
}
