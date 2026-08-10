using AdhdTimeOrganizer.Core.infrastructure.persistence.configuration.extensions;
using AdhdTimeOrganizer.TodoLists.domain.model.entity.todoList;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace AdhdTimeOrganizer.TodoLists.infrastructure.persistence.configuration.todoList;

public class TodoListCategoryConfiguration : IEntityTypeConfiguration<TodoListCategory>
{
    public void Configure(EntityTypeBuilder<TodoListCategory> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();
        builder.IsManyWithOneUser();
        builder.HasIndex(r => new { r.UserId, r.Name }).IsUnique();

        builder.HasMany(c => c.TodoListColl)
            .WithOne(tl => tl.Category)
            .HasForeignKey(tl => tl.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}