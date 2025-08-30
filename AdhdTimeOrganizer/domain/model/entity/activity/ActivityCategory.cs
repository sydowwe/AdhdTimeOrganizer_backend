using AdhdTimeOrganizer.domain.model.entity.@base;

namespace AdhdTimeOrganizer.domain.model.entity.activity;

public class ActivityCategory : BaseNameTextColorEntity
{
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
    // public string icon { get; set; }
}