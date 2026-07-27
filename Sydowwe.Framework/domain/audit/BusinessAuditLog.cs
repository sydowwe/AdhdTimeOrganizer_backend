using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.audit;

public class BusinessAuditLog : IEntityWithId
{
    public long Id { get; set; }
    public string EventType { get; set; } = null!;
    public string? Payload { get; set; }
    public string? EntityName { get; set; }
    public long? EntityId { get; set; }
    public long? UserId { get; set; }
    public string? CorrelationId { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}