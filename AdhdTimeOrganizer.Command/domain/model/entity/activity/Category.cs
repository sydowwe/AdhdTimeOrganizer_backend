using AdhdTimeOrganizer.Command.domain.model.entity.@base;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activity;

public class Category : BaseNameTextColorEntity
{
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
    // public string icon { get; set; }
}