using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Identity;
using Sydowwe.Framework.domain.entityInterface;
using Sydowwe.Framework.domain.@enum;

namespace Sydowwe.Framework.domain.entity.user;

public abstract class BaseUser : IdentityUser<long>, IEntityWithId
{
    public bool HasPassword => PasswordHash != null;

    public bool IsActive { get; set; } = true;
    public AppThemeEnum Theme { get; set; } = AppThemeEnum.Light;
    public AvailableLocales Locale { get; set; } = AvailableLocales.Sk;

    public TimeZoneInfo Timezone { get; set; } = TimeZoneInfo.Utc;

    public bool AskBeforeDelete { get; set; } = true;

    public DateTime CreatedTimestamp { get; set; } = DateTime.UtcNow;
    public DateTime ModifiedTimestamp { get; set; } = DateTime.UtcNow;

    [NotMapped]
    public override string? PhoneNumber { get; set; }

    [NotMapped]
    public override bool PhoneNumberConfirmed { get; set; }
}