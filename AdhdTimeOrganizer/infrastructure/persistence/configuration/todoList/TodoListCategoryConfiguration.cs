using AdhdTimeOrganizer.domain.model.entity.todoList;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.todoList;

public class TodoListCategoryConfiguration : IEntityTypeConfiguration<TodoListCategory>
{
    public void Configure(EntityTypeBuilder<TodoListCategory> builder)
    {
        builder.BaseNameTextColorIconEntityConfigure();
        builder.IsManyWithOneUser(u => u.TodoListCategoryColl);

        builder.HasMany(c => c.TodoListColl)
            .WithOne(tl => tl.Category)
            .HasForeignKey(tl => tl.CategoryId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
