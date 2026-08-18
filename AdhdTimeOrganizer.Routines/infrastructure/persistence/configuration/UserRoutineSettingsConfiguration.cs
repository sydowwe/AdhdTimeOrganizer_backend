using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.Routines.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.Routines.infrastructure.persistence.configuration.todoList;

public class UserRoutineSettingsConfiguration : IEntityTypeConfiguration<UserRoutineSettings>
{
    public void Configure(EntityTypeBuilder<UserRoutineSettings> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsOneWithOneUser<UserRoutineSettings>();

        builder.Property(s => s.RoutineReviewDismissedForWeekStart);
    }
}
