using System.Text.Json;
using AdhdTimeOrganizer.infrastructure.persistence.configuration.extensions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure.Internal;
using Npgsql.EntityFrameworkCore.PostgreSQL.Migrations;

namespace AdhdTimeOrganizer.infrastructure.persistence;

public class PartitionedNpgsqlMigrationsSqlGenerator(
    MigrationsSqlGeneratorDependencies dependencies,
    INpgsqlSingletonOptions npgsqlSingletonOptions)
    : NpgsqlMigrationsSqlGenerator(dependencies, npgsqlSingletonOptions)
{
    protected override void Generate(
        CreateTableOperation operation,
        IModel? model,
        MigrationCommandListBuilder builder,
        bool terminate = true)
    {
        // Annotations aren't propagated from entity type to CreateTableOperation,
        // so look them up from the model's entity types directly
        string? partitionColumn = null;
        string? partitionsJson = null;

        if (model != null)
        {
            var entityType = model.GetEntityTypes()
                .FirstOrDefault(e =>
                    e.GetTableName() == operation.Name
                    && (e.GetSchema() ?? "public") == (operation.Schema ?? "public"));

            if (entityType != null)
            {
                partitionColumn = entityType.FindAnnotation(PartitioningExtensions.AnnotationPartitionColumn)?.Value as string;
                partitionsJson = entityType.FindAnnotation(PartitioningExtensions.AnnotationPartitions)?.Value as string;
            }
        }

        if (partitionColumn is null)
        {
            base.Generate(operation, model, builder, terminate);
            return;
        }

        // Generate CREATE TABLE without terminating — we need to append PARTITION BY
        base.Generate(operation, model, builder, terminate: false);

        builder.AppendLine();
        builder.Append($"PARTITION BY RANGE ({Dependencies.SqlGenerationHelper.DelimitIdentifier(partitionColumn)})");
        builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
        builder.EndCommand();

        // Generate partition child tables
        if (partitionsJson is null) return;

        var partitions = JsonSerializer.Deserialize<List<PartitionDefinition>>(partitionsJson);
        if (partitions is null) return;

        var tableName = Dependencies.SqlGenerationHelper.DelimitIdentifier(operation.Name, operation.Schema);

        foreach (var partition in partitions)
        {
            var partitionName = Dependencies.SqlGenerationHelper.DelimitIdentifier(partition.Name, operation.Schema);

            if (partition.IsDefault)
            {
                builder.Append($"CREATE TABLE {partitionName} PARTITION OF {tableName} DEFAULT");
            }
            else
            {
                builder.Append(
                    $"CREATE TABLE {partitionName} PARTITION OF {tableName} FOR VALUES FROM ('{partition.From}') TO ('{partition.To}')");
            }

            builder.AppendLine(Dependencies.SqlGenerationHelper.StatementTerminator);
            builder.EndCommand();
        }
    }
}
