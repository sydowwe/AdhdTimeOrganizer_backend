using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;

public class UserTodoListSettingsConfiguration : IEntityTypeConfiguration<UserTodoListSettings>
{
    public void Configure(EntityTypeBuilder<UserTodoListSettings> builder)
    {
        builder.BaseEntityConfigure();
        builder.IsOneWithOneUser<UserTodoListSettings>();

        // No HasDefaultValue: with a database default EF treats the CLR default as "unset", so a first save of
        // 0 ("at the due time") would silently insert the default instead. The default lives on the entity.
        builder.Property(s => s.DueReminderMinutesBefore).IsRequired();
    }
}
