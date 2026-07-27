using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.helper;

namespace Sydowwe.Framework.infrastructure.persistence;

public static class PartitioningExtensions
{
    public const string AnnotationPartitionColumn = "Partitioning:Column";
    public const string AnnotationPartitions = "Partitioning:Partitions";

    /// <summary>
    /// Marks the table as partitioned by RANGE on the given column.
    /// Partition definitions are stored as model annotations and automatically
    /// applied when migrations are generated via <see cref="PartitionedNpgsqlMigrationsSqlGenerator"/>.
    /// </summary>
    public static void IsPartitionedByRange<TEntity>(
        this EntityTypeBuilder<TEntity> builder,
        string column,
        IReadOnlyList<PartitionDefinition> partitions) where TEntity : class
    {
        builder.HasAnnotation(AnnotationPartitionColumn, column);
        builder.HasAnnotation(AnnotationPartitions, JsonHelper.Serialize(partitions));
    }
}

public record PartitionDefinition(string Name, string? From, string? To)
{
    public bool IsDefault => From is null && To is null;
}