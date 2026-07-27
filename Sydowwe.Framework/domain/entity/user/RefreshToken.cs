using Sydowwe.Framework.domain.audit;
using Sydowwe.Framework.domain.entity.@base;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.entity.user;

[NoAudit]
public class RefreshToken : BaseTableEntity, IEntityWithUser
{
    public long UserId { get; set; }
    public required string TokenHash { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required bool StayLoggedIn { get; set; }
    public required AuthMethodEnum AuthMethod { get; set; }
    public bool IsExtensionClient { get; set; }
    public string? UserAgent { get; set; }
    public string? IpAddress { get; set; }
    public bool IsRevoked { get; set; }
    public string? ReplacedByTokenHash { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
}