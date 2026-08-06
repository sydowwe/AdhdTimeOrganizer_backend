using AdhdTimeOrganizer.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskImportance : BaseEntityWithUser, IBaseTextColorIconEntity
{
    public required string Text { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }

    public required int Importance { get; set; }
}