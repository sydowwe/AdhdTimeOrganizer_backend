using AdhdTimeOrganizer.domain.model.entity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;

public static class TodoListEntityConfigurationExtensions
{
    public static EntityTypeBuilder<TEntity> BaseTodoListConfigure<TEntity>(this EntityTypeBuilder<TEntity> builder) where TEntity : BaseTodoList
    {
        var entityName = typeof(TEntity).Name;
        builder.ToTable(t =>
        {
            t.HasCheckConstraint($"CK_{entityName}_DoneCount_Min", "done_count IS NULL OR done_count >= 0");
            t.HasCheckConstraint($"CK_{entityName}_TotalCount_Range", "total_count IS NULL OR total_count >= 2 AND total_count <= 99");
            t.HasCheckConstraint($"CK_{entityName}_DoneCount_LessOrEqual_TotalCount", "done_count <= total_count");
        });

        builder.Property(e=> e.DisplayOrder).IsRequired();
        builder.Property(p => p.IsDone).HasDefaultValue(false).IsRequired();

        return builder;
    }
}