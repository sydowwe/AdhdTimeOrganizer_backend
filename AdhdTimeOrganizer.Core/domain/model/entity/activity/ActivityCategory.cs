using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.Core.domain.model.entity.activity;

public class ActivityCategory : BaseEntityWithUser, IBaseNameTextColorIconEntity
{
    public required string Name { get; set; }
    public string? Text { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }

    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();

    // The tracker-mapping inverse collections were removed so the activity area stops referencing the
    // tracking area; Tracker{Desktop,Android}MappingByPatternConfiguration configures those FKs from
    // the dependent side with a bare .WithMany(). No column or cascade changed.
}