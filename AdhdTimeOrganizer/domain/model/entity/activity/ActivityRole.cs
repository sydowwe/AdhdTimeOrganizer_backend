using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class ActivityRole : BaseNameTextColorIconEntity
{
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
}