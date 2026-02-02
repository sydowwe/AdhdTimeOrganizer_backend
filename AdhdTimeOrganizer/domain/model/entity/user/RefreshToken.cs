using AdhdTimeOrganizer.domain.extServiceContract.user.auth;

namespace AdhdTimeOrganizer.domain.model.entity.user;

public class RefreshToken : BaseEntityWithUser
{
    public required string TokenHash { get; set; }
    public required DateTime ExpiresAt { get; set; }
    public required bool StayLoggedIn { get; set; }
    public required bool IsExtensionClient { get; set; }
    public required AuthMethodEnum AuthMethod { get; set; }
    public bool IsRevoked { get; set; } = false;
    public string? ReplacedByTokenHash { get; set; }
    public DateTime? RevokedAt { get; set; }
    public string? RevokedByIp { get; set; }
}
