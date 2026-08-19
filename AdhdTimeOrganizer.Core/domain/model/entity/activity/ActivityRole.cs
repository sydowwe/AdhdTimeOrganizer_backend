using AdhdTimeOrganizer.Core.domain.model.@enum;
using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entity.activity;

public class ActivityRole : BaseEntityWithUser, IBaseNameTextColorIconEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }

    /// <summary>
    /// Set on the three roles the app itself looks up, <c>null</c> on every user-created role. This
    /// is the stable identity <see cref="Name"/> is not: the user may rename a keyed role freely and
    /// the key survives. Only the seeder writes it — no request DTO carries it, so a keyed role can
    /// neither be created nor un-keyed through the API. See <see cref="SystemActivityRole"/>.
    /// </summary>
    public SystemActivityRole? SystemKey { get; set; }

    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();

    // The tracker-mapping inverse collections were removed so the activity area stops referencing the
    // tracking area; Tracker{Desktop,Android}MappingByPatternConfiguration configures those FKs from
    // the dependent side with a bare .WithMany(). No column or cascade changed.
}