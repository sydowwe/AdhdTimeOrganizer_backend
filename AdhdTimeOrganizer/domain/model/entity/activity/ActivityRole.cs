using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class ActivityRole : BaseNameTextColorEntity
{
    public string? Icon { get; set; }
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
}