using AdhdTimeOrganizer.domain.model.entity.@base.core;
using AdhdTimeOrganizer.domain.model.entity.activity.lookup;

namespace AdhdTimeOrganizer.domain.model.entity.activity.profile;

public class ActivityBucketListProfile : BaseTableEntity
{
    public long ActivityId { get; set; }
    public Activity Activity { get; set; } = null!;

    public long ExperienceTypeId { get; set; }
    public ActivityExperienceType ExperienceType { get; set; } = null!;
    public int ComfortZoneStep { get; set; }
    public bool RequiresTravel { get; set; }
    public decimal? FinancialGoal { get; set; }
    public string InspirationSource { get; set; } = null!;
}
