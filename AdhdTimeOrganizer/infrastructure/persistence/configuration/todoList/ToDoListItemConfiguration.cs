using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;
using Sydowwe.Framework.infrastructure.persistence.converter;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class TodoListItemConfiguration : IEntityTypeConfiguration<TodoListItem>
{
    public void Configure(EntityTypeBuilder<TodoListItem> builder)
    {
        builder.BaseEntityConfigure();

        builder.BaseTodoListConfigure();

        builder.Property(t => t.SuggestedTime)
            .HasConversion(new NullableIntTimeConverter());

        builder.IsManyWithOneUser();
        builder.IsManyWithOneActivity();

        builder.HasOne(r => r.TaskPriority)
            .WithMany(t => t.TodoListColl)
            .HasForeignKey(r => r.TaskPriorityId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.Property(t => t.DueDate).IsRequired(false);
        builder.Property(t => t.DueTime).IsRequired(false);

        builder.HasIndex(t => new { t.UserId, t.ActivityId, t.TodoListId })
            .IsUnique();

        builder.HasIndex(t => new { t.UserId, t.TaskPriorityId });
    }
}