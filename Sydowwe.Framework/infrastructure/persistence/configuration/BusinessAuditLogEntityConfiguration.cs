using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Sydowwe.Framework.domain.audit;

namespace Sydowwe.Framework.infrastructure.persistence.configuration;

public class BusinessAuditLogEntityConfiguration : IEntityTypeConfiguration<BusinessAuditLog>
{
    public void Configure(EntityTypeBuilder<BusinessAuditLog> builder)
    {
        builder.ToTable("business_audit_log", "audit");

        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseSerialColumn();

        builder.Property(x => x.EventType).IsRequired().HasMaxLength(200);
        builder.Property(x => x.Payload).HasColumnType("jsonb");
        builder.Property(x => x.EntityName).HasMaxLength(200);
        builder.Property(x => x.EntityId);
        builder.Property(x => x.UserId);
        builder.Property(x => x.CorrelationId).HasMaxLength(64);
        builder.Property(x => x.Timestamp).HasDefaultValueSql("now()").IsRequired();

        builder.HasIndex(x => x.EventType);
        builder.HasIndex(x => new { x.EntityName, x.EntityId });
        builder.HasIndex(x => x.Timestamp).IsDescending();
        builder.HasIndex(x => x.UserId);
        builder.HasIndex(x => x.CorrelationId);
    }
}