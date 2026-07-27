using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.audit;

public class AuditLog : IEntityWithId
{
    public long Id { get; set; }
    public string EntityName { get; set; } = null!;
    public long EntityId { get; set; }
    public AuditActionEnum Action { get; set; }
    public string? Before { get; set; }
    public string? After { get; set; }
    public string[]? ChangedProperties { get; set; }
    public long? UserId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public DateOnly Date { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow);
}