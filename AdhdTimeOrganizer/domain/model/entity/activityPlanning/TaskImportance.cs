using AdhdTimeOrganizer.Core.domain.model.entity.user;
using Sydowwe.Framework.domain.entityInterface;

namespace AdhdTimeOrganizer.domain.model.entity.activityPlanning;

public class TaskImportance : BaseEntityWithUser, IBaseTextColorIconEntity
{
    /// <summary>Rank value seeded for the default "Optional" level (see <see cref="BasePlannerTask.IsOptional"/>).</summary>
    public const int OptionalMarkerValue = 666;

    /// <summary>Rank value seeded for the default "Critical" level.</summary>
    public const int CriticalMarkerValue = 999;

    public required string Text { get; set; }
    public string? Color { get; set; }
    public string? Icon { get; set; }

    public required int Importance { get; set; }
}