using AdhdTimeOrganizer.domain.model.entity.activityPlanning;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.activityPlanning;

public class CalendarConfiguration : IEntityTypeConfiguration<Calendar>
{
    public void Configure(EntityTypeBuilder<Calendar> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsManyWithOneUser(u => u.Calendar);
        builder.EnumColumn(c => c.DayType);

        builder.Property(c => c.Date).IsRequired();

        builder.Property(c => c.HolidayName).HasMaxLength(200);
        builder.Property(c => c.Label).HasMaxLength(100);
        builder.Property(c => c.AppliedTemplateName).HasMaxLength(200);
        builder.Property(c => c.Weather).HasMaxLength(100);
        builder.Property(c => c.Notes).HasMaxLength(1000);

        builder.HasIndex(c => new { c.UserId, c.Date }).IsUnique();
        builder.HasIndex(c => c.DayType);
    }
}
