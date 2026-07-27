using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.domain.entityInterface;

namespace Sydowwe.Framework.domain.entity.user;

public class UserRole : IdentityRole<long>, IEntityWithId
{
    public required string Description { get; set; }
    public required bool IsDefault { get; set; }
    public required int RoleLevel { get; set; }
    public required bool IsAssignable { get; set; }
    public DateTime CreatedDate { get; set; } = DateTime.UtcNow;
}