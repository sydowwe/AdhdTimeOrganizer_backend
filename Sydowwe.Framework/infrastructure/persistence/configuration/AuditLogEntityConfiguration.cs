using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.infrastructure.persistence.configuration.extensions;

namespace Sydowwe.Framework.infrastructure.persistence.configuration;

public class AuditLogEntityConfiguration : IEntityTypeConfiguration<AuditLog>
{
    private const int FirstYear = 2026;
    private const int YearCount = 10;

    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log", "audit");

        // Composite PK required by Postgres partitioning: partition column must be in PK.
        builder.HasKey(x => new { x.Id, x.Date });
        builder.Property(x => x.Id).UseSerialColumn();

        builder.Property(x => x.EntityName).IsRequired().HasMaxLength(200);
        builder.Property(x => x.EntityId).IsRequired();
        builder.EnumColumn(x => x.Action);

        builder.Property(x => x.Before).HasColumnType("jsonb");
        builder.Property(x => x.After).HasColumnType("jsonb");
        builder.Property(x => x.ChangedProperties).HasColumnType("text[]");

        builder.Property(x => x.UserId);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Timestamp).HasDefaultValueSql("now()").IsRequired();
        builder.Property(x => x.Date).IsRequired();

        builder.HasIndex(x => new { x.EntityName, x.EntityId });
        builder.HasIndex(x => x.Timestamp).IsDescending();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CorrelationId);

        builder.IsPartitionedByRange("date", BuildYearlyPartitions(FirstYear, YearCount));
    }

    private static IReadOnlyList<PartitionDefinition> BuildYearlyPartitions(int startYear, int years)
    {
        var list = new List<PartitionDefinition>(years + 1);
        for (var i = 0; i < years; i++)
        {
            var year = startYear + i;
            list.Add(new PartitionDefinition(
                $"audit_log_{year}",
                $"{year}-01-01",
                $"{year + 1}-01-01"));
        }

        list.Add(new PartitionDefinition("audit_log_default", null, null));
        return list;
    }
}