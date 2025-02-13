using AdhdTimeOrganizer.Command.domain.model.entity.@base;

namespace AdhdTimeOrganizer.Command.domain.model.entity.activity;

public class Role : BaseNameTextColorEntity
{
    public string? Icon { get; set; }
    public virtual ICollection<Activity> Activities { get; set; } = new List<Activity>();
}